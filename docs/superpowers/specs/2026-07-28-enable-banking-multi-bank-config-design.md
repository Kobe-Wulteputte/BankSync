# Enable Banking: configurable multi-bank accounts

Date: 2026-07-28
Status: Approved, ready for implementation planning

## Problem

`EnableBankingService.CreateNewAccountCheck` hardcodes two Revolut IBANs, along with
the ASPSP name, country, PSU type, consent length, and redirect URL. Connecting an
account at a different bank means editing and rebuilding the application.

Three further limits block a multi-bank setup:

- `SessionKeyStore` holds bare session GUIDs with no record of which bank or IBANs a
  session covers, so every run re-derives that from the API — `GetSession` plus
  `GetDetails` for each session/IBAN pair.
- Both the sync and the connect path read `session.Data.Accounts[0]`, so only the first
  account of a session is ever reachable.
- Transaction pagination is broken in two compounding ways. `GetTransactionsResponse`
  declares `[JsonProperty("continuationKey")]`, but the API returns `continuation_key` —
  snake_case, as every other model in the codebase uses and as `AccountsService` already
  sends it. The key therefore never binds, and the `do/while` in `GetEnableTransactions`
  exits after one page, silently truncating results. Correcting the property name alone
  would make things worse: the loop never feeds the key into the next request, so it
  would then spin forever re-fetching the first page. Both halves must be fixed together.

## Goals

- Configure any number of accounts across any number of banks from `appsettings.json`.
- Never block an unattended sync run on interactive authorization.
- One session per bank, covering all of that bank's configured accounts.

## Non-goals

- Automatic discovery of accounts a bank exposes but config does not list.
- Any change to Nordigen/GoCardless or Edenred import paths.
- Automated tests: the solution has no test project, and adding one is out of scope here.

## Design

### Configuration

New `BS.Logic/EnableBanking/Config/EnableBankingSettings.cs`, bound from the existing
`EnableBanking` section. This follows the precedent set by `BS.Edenred/Config/EdenredSettings.cs`.

```csharp
public sealed class EnableBankingSettings
{
    public string KeyPath { get; set; } = "enablebanking.key";
    public string AppKid { get; set; } = string.Empty;
    public Uri RedirectUrl { get; set; } = new("https://localhost:8080");
    public int ConsentValidityDays { get; set; } = 90;
    public int RenewBeforeDays { get; set; } = 1;
    public List<BankSettings> Banks { get; set; } = [];
}

public sealed class BankSettings
{
    public string Name { get; set; } = string.Empty;      // ASPSP name, e.g. "Revolut"
    public string Country { get; set; } = string.Empty;   // e.g. "BE"
    public string PsuType { get; set; } = "personal";
    public int? ConsentValidityDays { get; set; }         // overrides the global value
    public List<string> Ibans { get; set; } = [];
}
```

Resulting `appsettings.json` shape:

```json
"EnableBanking": {
  "KeyPath": "4a77f9d9-84e2-448e-a9bd-01075cb9ddc7.pem",
  "AppKid": "4a77f9d9-84e2-448e-a9bd-01075cb9ddc7",
  "RedirectUrl": "https://localhost:8080",
  "ConsentValidityDays": 90,
  "RenewBeforeDays": 1,
  "Banks": [
    {
      "Name": "Revolut",
      "Country": "BE",
      "PsuType": "personal",
      "Ibans": [ "BE29650184652964", "BE50650280329118" ]
    }
  ]
}
```

`ConsentValidityDays` is overridable per bank because ASPSPs cap consent length
differently — the API exposes this as `Aspsp.MaximumConsentValidity`. A flat 90 days is
rejected by some banks.

`Program.cs` binds the section with
`services.Configure<EnableBankingSettings>(ctx.Configuration.GetSection("EnableBanking"))`
and sources `KeyPath`/`AppKid` for `AddEnableBankingApi` from the same bound instance,
so the two are never read from different places.

Startup validation rejects, with a message naming the offending entry:

- a bank with an empty `Name` or `Country`
- a bank with an empty `Ibans` list
- the same IBAN listed under two banks

IBANs are normalised on load — uppercased, whitespace removed — so config formatting
cannot cause a silent match failure.

`RetrievalDays` stays a top-level configuration key. `BS.Logic/Nordigen/AccountService.cs`
reads it too, so it is not Enable Banking-specific.

### Session store

`SessionKeyStore` moves from a line-delimited GUID list to a JSON array of records. It
stays pure persistence — it performs no API calls.

```csharp
public sealed class BankSession
{
    public Guid SessionId { get; set; }
    public string Bank { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public List<StoredAccount> Accounts { get; set; } = [];
    public DateTime? ValidUntil { get; set; }
}

public sealed class StoredAccount
{
    public Guid Uid { get; set; }
    public string Iban { get; set; } = string.Empty;
}
```

Storing the account `Uid` alongside the IBAN is what removes `GetDetails` from the sync
path: a configured IBAN maps directly to the account id needed by
`GetTransactionsRequest`. Both values come from `AuthorizeSessionResponse.Accounts`,
which already carries `Uid` and `AllAccountIds`.

The file path stays `FilePaths:SessionKeys`, which already points at `sessionkeys.json`.

**Legacy files.** Rather than a separate migration routine, the store detects a
non-JSON file and parses it as bare GUIDs into records with only `SessionId` populated.
`EnableBankingService` fills the remaining fields from the API on the next run and
saves. The same code path repairs a record whose metadata is missing or stale, so the
existing sessions survive without re-authorizing.

### Connect command

A new `Connect` argument branch in `Program.cs`, alongside the existing
`GenerateTrainingData` branch, calling `EnableBankingService.ConnectAsync()`:

1. Load the store; for any record missing metadata, resolve bank and accounts from the
   API and save.
2. Remove from the local store any record that errors on `GetSession`, and any whose
   `Access.ValidUntil` falls within `RenewBeforeDays` of now. These are local removals
   only — a session that errors cannot be deleted remotely, and an expiring one lapses
   on its own.
3. For each configured bank, compare its configured IBANs against those covered by its
   live session.
4. If any are missing, call `StartAuthorization` for that bank with `Access.Accounts`
   set to **all** of the bank's configured IBANs, `ValidUntil` set to now plus the
   bank's override or the global `ConsentValidityDays`, and aspsp name, country,
   PSU type and redirect URL taken from config.
5. Log the authorization URL, read the code from stdin, call `AuthorizeSession`, and
   save a `BankSession` built from the response. Only then call `DeleteSession` on the
   bank's previous session, if step 2 left one in place — that is, when the old session
   was still live but covered only some of the configured IBANs. Deleting after the new
   session is saved means a failed authorization never leaves the bank with no session.
6. On error, log and continue to the next bank.

**Invariant: at most one live session per configured bank.** Step 4 requests the full
IBAN set rather than only the missing ones so a new consent supersedes the old partial
one; step 5's delete enforces the invariant. Without it, two overlapping consents would
both yield the same transactions.

### Sync path

`Application.Run()` no longer calls the connect flow — the line
`await enableBankingService.CreateNewAccountCheck();` is removed.

`GetEnableTransactions` becomes:

- For each configured bank, look up its stored session. If absent or expired, log a
  warning naming the bank and telling the user to run `BankSync Connect`, then continue
  to the next bank. Remaining banks still sync.
- Resolve each configured IBAN to its account `Uid` via the stored record. If the
  session does not cover it, warn and skip that account.
- Fetch transactions per account with pagination fixed on both sides: retag
  `GetTransactionsResponse.ContinuationKey` as `[JsonProperty("continuation_key")]` so
  it binds at all, and pass the previous response's key into the next
  `GetTransactionsRequest`, stopping when it comes back null. `AccountsService` already
  puts `continuation_key` on the query string, so no transport change is needed.
- `DateFrom` continues to come from `RetrievalDays`.

`ExpenseService.CreateExpense(Transaction, GetSessionResponse)` uses the session only for
`Aspsp.Name`, which becomes `Expense.Type`. It changes to take the bank name as a
`string`, matching the existing `CreateExpense(BookedTransaction, string accountName)`
overload. There is one call site.

### Error handling

Failures are per-bank and never abort the whole run:

| Condition | Behaviour |
| --- | --- |
| Invalid configuration | Throw at startup with the offending bank named |
| `ValidateConnection` fails | Return empty, as today |
| `GetSession` errors during connect | Drop the record, re-authorize that bank |
| `GetSession` errors during sync | Warn, skip the bank, continue |
| Session missing for a bank | Warn naming the bank, skip, continue |
| Session does not cover a configured IBAN | Warn naming the IBAN, skip that account |
| `AuthorizeSession` fails | Log, keep the old session, continue to the next bank |

## Project structure note

`BS.Logic/EnableBanking/EnableBanking.csproj` is a vendored copy of the third-party
`EnableBanking` client that was dropped inside the `BS.Logic` folder. It is not listed in
`BankSync.sln` and nothing references it. Because SDK-style projects glob `**/*.cs`, its
sources compile straight into the `BS.Logic` assembly, which is why
`using EnableBanking;` resolves in `Application.cs` with no project reference.

Consequences for this work:

- New Enable Banking files placed under `BS.Logic/EnableBanking/` compile into `BS.Logic`.
  No project reference or solution change is needed.
- Package availability is governed by `BS.Logic.csproj`, not `EnableBanking.csproj`.
  `IOptions<T>` already resolves there transitively, so no new package is required.
- Editing the vendored model `GetTransactionsResponse.cs` is safe — nothing consumes the
  orphaned project, and the file is not restored from NuGet.

Untangling that vendored project is out of scope here.

## Files touched

New:

- `BS.Logic/EnableBanking/Config/EnableBankingSettings.cs` (both settings classes)
- `BS.Data/BankSession.cs`

Modified:

- `BS.Data/SessionKeyStore.cs` — JSON records, legacy read
- `BS.Logic/EnableBanking/EnableBankingService.cs` — `ConnectAsync`, config-driven sync,
  pagination loop fix
- `BS.Logic/EnableBanking/Models/Accounts/GetTransactionsResponse.cs` — retag
  `ContinuationKey` as `continuation_key`
- `BS.Logic/Workbook/ExpenseService.cs` — overload takes bank name
- `BS.Logic/Application.cs` — drop the connect call
- `BS.Console/Program.cs` — bind settings, add `Connect` branch
- `BS.Console/appsettings.json` — new `EnableBanking:Banks` shape

## Verification

Manual, since the solution has no test project:

1. Run `BankSync Connect` with only the two existing Revolut IBANs configured. The
   legacy session file should self-heal into JSON records with bank and IBANs filled in,
   and no re-authorization should be prompted.
2. Add a second bank to config and run `BankSync Connect`. Only the new bank should
   prompt; Revolut's session should be left alone.
3. Run a normal sync. Transactions from both banks appear, and `Expense.Type` shows the
   correct bank name per row.
4. Remove a bank's session file entry and run a normal sync. It should warn about that
   bank and still sync the other, without blocking on stdin.

## Decisions and open assumptions

- **Only configured IBANs sync**, even where a bank's consent exposes more accounts.
  Adding an account at an existing bank therefore requires a config edit and a reconnect.
- **One session per bank**, enforced by deleting the prior session after a successful
  re-authorization.
- Alternatives considered and rejected: a flat per-IBAN account list, which costs one
  authorization flow per account at the same bank; and bank-only config with no IBAN
  list, which removes the ability to exclude accounts from syncing.
