# Configurable Multi-Bank Enable Banking Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded two-IBAN Revolut list with an `appsettings.json`-driven bank list, so any number of accounts across any number of banks can be synced.

**Tracking:** none

**Architecture:** Config gains an `EnableBanking:Banks` array, each entry naming an ASPSP plus the IBANs to sync there. One Enable Banking session per bank covers all of that bank's accounts. `SessionKeyStore` becomes a JSON store recording session id, bank, covered IBANs and account UIDs, so the sync path resolves an IBAN to an account UID with no API round-trips. Interactive authorization moves behind a `BankSync Connect` argument so unattended sync runs never block on stdin.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Options` / configuration binding, `System.Text.Json` (BCL, no new package), vendored `EnableBanking` API client, Serilog.

**Spec:** `docs/superpowers/specs/2026-07-28-enable-banking-multi-bank-config-design.md`

---

## Testing note — read before starting

The writing-plans skill defaults to TDD with a named test level per task. **This plan deviates deliberately.** `BankSync.sln` contains four projects (`BS.Console`, `BS.Logic`, `BS.Data`, `BS.Edenred`) and no test project, and the approved spec lists adding one as an explicit non-goal. Every task below therefore verifies with `dotnet build BankSync.sln` plus, where behaviour changes, a targeted manual run.

The pieces most worth unit testing if that decision is ever revisited are pure functions with no I/O: `EnableBankingSettings.NormalizeIban`, `EnableBankingSettings.NormalizeAndValidate`, and `SessionKeyStore`'s JSON/legacy round-trip. Those three are where a silent regression is most likely and would be cheap to cover.

## Project structure — important context

`BS.Logic/EnableBanking/EnableBanking.csproj` is a vendored third-party client dropped inside the `BS.Logic` folder. **It is not in `BankSync.sln` and nothing references it.** SDK-style globbing compiles its `.cs` files directly into the `BS.Logic` assembly, which is why `using EnableBanking;` resolves in `Application.cs` with no project reference.

Practical consequences:

- New files under `BS.Logic/EnableBanking/` compile into `BS.Logic`. No solution or project-reference change needed.
- Package availability is governed by `BS.Logic.csproj`. `IOptions<T>` already resolves there transitively — do not add a package for it.
- Editing the vendored `GetTransactionsResponse.cs` (Task 4) is safe; nothing restores it from NuGet.

Baseline before starting: `dotnet build BankSync.sln` reports **0 Errors, 44 Warnings**. The warning count should not rise.

## File structure

| File | Responsibility |
| --- | --- |
| `BS.Logic/EnableBanking/Config/EnableBankingSettings.cs` (new) | Bound settings + IBAN normalization + startup validation |
| `BS.Data/BankSession.cs` (new) | Persisted session record: id, bank, accounts, expiry |
| `BS.Data/SessionKeyStore.cs` | JSON persistence of `BankSession` records, legacy GUID-list read |
| `BS.Logic/EnableBanking/EnableBankingService.cs` | `ConnectAsync` (interactive) and `GetEnableTransactions` (unattended) |
| `BS.Logic/EnableBanking/Models/Accounts/GetTransactionsResponse.cs` | Fix `continuation_key` JSON name |
| `BS.Logic/Workbook/ExpenseService.cs` | Overload takes bank name instead of session |
| `BS.Logic/Application.cs` | Stop calling the interactive flow during sync |
| `BS.Console/Program.cs` | Bind settings, add `Connect` argument branch |
| `BS.Console/appsettings.json` | New `EnableBanking:Banks` shape |

---

### Task 1: Settings model with normalization and validation

**Files:**
- Create: `BS.Logic/EnableBanking/Config/EnableBankingSettings.cs`

**Test level:** Manual verification via build, because this task adds types only — no behaviour is wired up until Task 2. Validation logic is exercised manually in Task 2 Step 4.

- [ ] **Step 1: Create the settings file**

Create `BS.Logic/EnableBanking/Config/EnableBankingSettings.cs`:

```csharp
namespace EnableBanking.Config;

/// <summary>Bound from the "EnableBanking" section of appsettings.json.</summary>
public sealed class EnableBankingSettings
{
    public string KeyPath { get; set; } = "enablebanking.key";
    public string AppKid { get; set; } = string.Empty;
    public Uri RedirectUrl { get; set; } = new("https://localhost:8080");

    /// <summary>Consent length requested when authorizing. Overridable per bank.</summary>
    public int ConsentValidityDays { get; set; } = 90;

    /// <summary>A session this close to expiry is treated as needing re-authorization.</summary>
    public int RenewBeforeDays { get; set; } = 1;

    public List<BankSettings> Banks { get; set; } = [];

    /// <summary>
    /// Normalizes every configured IBAN in place and throws on configuration that cannot work.
    /// Call once at startup so problems surface before any API call is made.
    /// </summary>
    public void NormalizeAndValidate()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bank in Banks)
        {
            if (string.IsNullOrWhiteSpace(bank.Name))
                throw new InvalidOperationException("EnableBanking:Banks contains an entry with no Name.");

            if (string.IsNullOrWhiteSpace(bank.Country))
                throw new InvalidOperationException($"EnableBanking:Banks entry '{bank.Name}' has no Country.");

            bank.Ibans = bank.Ibans.Select(NormalizeIban).Where(iban => iban.Length > 0).ToList();

            if (bank.Ibans.Count == 0)
                throw new InvalidOperationException($"EnableBanking:Banks entry '{bank.Name}' has no Ibans.");

            foreach (var iban in bank.Ibans)
            {
                if (owners.TryGetValue(iban, out var owner))
                    throw new InvalidOperationException($"IBAN {iban} is listed under both '{owner}' and '{bank.Name}'.");

                owners[iban] = bank.Name;
            }
        }

        var duplicate = Banks
            .GroupBy(bank => bank.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
            throw new InvalidOperationException(
                $"EnableBanking:Banks lists '{duplicate.Key}' more than once; put all its IBANs under a single entry.");
    }

    /// <summary>Strips spaces and punctuation and uppercases, so config formatting cannot cause a silent mismatch.</summary>
    public static string NormalizeIban(string? iban) =>
        new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

public sealed class BankSettings
{
    /// <summary>ASPSP name exactly as Enable Banking lists it, e.g. "Revolut".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Two-letter country code, e.g. "BE".</summary>
    public string Country { get; set; } = string.Empty;

    public string PsuType { get; set; } = "personal";

    /// <summary>Overrides the global value. Needed because ASPSPs cap consent length differently.</summary>
    public int? ConsentValidityDays { get; set; }

    public List<string> Ibans { get; set; } = [];
}
```

- [ ] **Step 2: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: `0 Error(s)`, warning count still 44.

- [ ] **Step 3: Commit**

```bash
git add BS.Logic/EnableBanking/Config/EnableBankingSettings.cs
git commit -m "feat: add EnableBankingSettings with IBAN normalization and validation"
```

---

### Task 2: Bind settings in DI and reshape appsettings.json

**Files:**
- Modify: `BS.Console/Program.cs:35-39`
- Modify: `BS.Console/appsettings.json:27-30`

**Test level:** Manual verification by running the app, because the risk being proven is that configuration binding and startup validation actually fire — a build cannot show that.

- [ ] **Step 1: Reshape the EnableBanking section in appsettings.json**

Replace the existing `"EnableBanking"` block (lines 27-30) with:

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
        "Ibans": [
          "BE29650184652964",
          "BE50650280329118"
        ]
      }
    ]
  },
```

- [ ] **Step 2: Bind the section in Program.cs**

Add to the using block at the top of `BS.Console/Program.cs`:

```csharp
using EnableBanking.Config;
using Microsoft.Extensions.Options;
```

Replace the `services.AddEnableBankingApi(...)` call (currently lines 35-39) with:

```csharp
        var enableBankingSettings = new EnableBankingSettings();
        ctx.Configuration.GetSection("EnableBanking").Bind(enableBankingSettings);
        enableBankingSettings.NormalizeAndValidate();
        services.AddSingleton(Options.Create(enableBankingSettings));
        services.AddEnableBankingApi(options =>
        {
            options.KeyPath = enableBankingSettings.KeyPath;
            options.AppKid = enableBankingSettings.AppKid;
        });
```

`KeyPath` and `AppKid` now come from the bound instance rather than a second `ctx.Configuration[...]` read, so the two can never drift.

- [ ] **Step 3: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Verify validation fires**

Temporarily add a duplicate IBAN to a second bank entry in `appsettings.json`:

```json
      { "Name": "KBC", "Country": "BE", "Ibans": [ "BE29650184652964" ] }
```

Run:

```bash
dotnet run --project BS.Console -- Connect
```

Expected: startup throws `InvalidOperationException` reading
`IBAN BE29650184652964 is listed under both 'Revolut' and 'KBC'.`
Then remove that temporary entry and confirm the app starts normally.

`Connect` is not implemented until Task 9, so at this point the run will fall through to the normal sync path after startup. That is expected — this step only proves validation runs.

- [ ] **Step 5: Commit**

```bash
git add BS.Console/Program.cs BS.Console/appsettings.json
git commit -m "feat: bind EnableBanking settings section and validate at startup"
```

---

### Task 3: BankSession record and JSON session store

**Files:**
- Create: `BS.Data/BankSession.cs`
- Modify: `BS.Data/SessionKeyStore.cs` (full rewrite)

**Test level:** Manual verification via a round-trip run in Task 10, because the store's only consumer is `EnableBankingService`. The legacy-parse branch is proven in Task 10 Step 1 against the real existing session file.

- [ ] **Step 1: Create the record type**

Create `BS.Data/BankSession.cs`:

```csharp
namespace BS.Data;

/// <summary>One Enable Banking session, covering every account authorized at a single bank.</summary>
public sealed class BankSession
{
    public Guid SessionId { get; set; }

    /// <summary>ASPSP name, matching EnableBanking:Banks[].Name.</summary>
    public string Bank { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public List<StoredAccount> Accounts { get; set; } = [];

    public DateTime? ValidUntil { get; set; }

    /// <summary>True when this record still needs its metadata resolved from the API.</summary>
    public bool IsIncomplete => string.IsNullOrWhiteSpace(Bank) || Accounts.Count == 0;
}

public sealed class StoredAccount
{
    /// <summary>Enable Banking account UID, used directly as GetTransactionsRequest.AccountId.</summary>
    public Guid Uid { get; set; }

    public string Iban { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Rewrite the store**

Replace the entire contents of `BS.Data/SessionKeyStore.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace BS.Data;

/// <summary>
/// Persists <see cref="BankSession"/> records as JSON. Pure persistence — performs no API calls.
/// Reads the historical one-GUID-per-line format too, yielding incomplete records that the
/// caller fills in from the API and saves back.
/// </summary>
public class SessionKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public SessionKeyStore(IConfiguration configuration)
    {
        _filePath = configuration["FilePaths:SessionKeys"] ?? "session-keys.json";
    }

    public List<BankSession> GetSessions()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var text = File.ReadAllText(_filePath).Trim();
        if (text.Length == 0)
        {
            return [];
        }

        if (text[0] == '[')
        {
            return JsonSerializer.Deserialize<List<BankSession>>(text, JsonOptions) ?? [];
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => Guid.TryParse(line, out _))
            .Select(line => new BankSession { SessionId = Guid.Parse(line) })
            .ToList();
    }

    public void SaveSessions(List<BankSession> sessions)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(sessions, JsonOptions));
    }

    /// <summary>Stores a session, replacing any existing session for the same bank.</summary>
    public void AddOrReplace(BankSession session)
    {
        var sessions = GetSessions()
            .Where(existing => !string.Equals(existing.Bank, session.Bank, StringComparison.OrdinalIgnoreCase))
            .ToList();

        sessions.Add(session);
        SaveSessions(sessions);
    }
}
```

The old `GetIds`, `AddId`, `RemoveId`, `ClearIds` and `SaveIds` methods are gone. Their only callers are in `EnableBankingService`, rewritten in Tasks 6-8.

- [ ] **Step 3: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: **errors in `EnableBankingService.cs`** for `GetIds`, `AddId` and `RemoveId` — those call sites are replaced in Tasks 6-8. This is the one task that intentionally leaves the tree red.

- [ ] **Step 4: Commit**

```bash
git add BS.Data/BankSession.cs BS.Data/SessionKeyStore.cs
git commit -m "feat: store bank sessions as JSON records with legacy GUID-list read"
```

---

### Task 4: Fix the continuation key JSON property name

**Files:**
- Modify: `BS.Logic/EnableBanking/Models/Accounts/GetTransactionsResponse.cs:10-11`

**Test level:** Manual verification in Task 10, because binding only fails against a real paged API response. The compile-time change is trivial; the risk is behavioural.

- [ ] **Step 1: Retag the property**

In `BS.Logic/EnableBanking/Models/Accounts/GetTransactionsResponse.cs`, change:

```csharp
        [JsonProperty("continuationKey")]
        public string? ContinuationKey { get; set; }
```

to:

```csharp
        [JsonProperty("continuation_key")]
        public string? ContinuationKey { get; set; }
```

Enable Banking returns snake_case, as every other model in this client uses and as `AccountsService.GetTransactionsAsync` already sends it on the query string. Until now the key never bound, so `GetEnableTransactions` silently stopped after the first page.

**This change alone makes the existing `do/while` loop infinite** — it never feeds the key into the next request. Task 8 fixes the loop. Do not run a sync between this task and Task 8.

- [ ] **Step 2: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: same `EnableBankingService.cs` errors as Task 3, no new ones.

- [ ] **Step 3: Commit**

```bash
git add BS.Logic/EnableBanking/Models/Accounts/GetTransactionsResponse.cs
git commit -m "fix: bind Enable Banking continuation_key from snake_case response field"
```

---

### Task 5: ExpenseService takes a bank name

**Files:**
- Modify: `BS.Logic/Workbook/ExpenseService.cs:3`, `BS.Logic/Workbook/ExpenseService.cs:63-64`

**Test level:** Manual verification in Task 10 Step 3, where `Expense.Type` is checked in the workbook. The risk is a wrong value landing in the sheet, which only a real run shows.

- [ ] **Step 1: Change the signature**

In `BS.Logic/Workbook/ExpenseService.cs`, change line 63 from:

```csharp
    public Expense CreateExpense(EnableBanking.Models.Accounts.Transaction transaction, GetSessionResponse session)
```

to:

```csharp
    public Expense CreateExpense(EnableBanking.Models.Accounts.Transaction transaction, string bankName)
```

- [ ] **Step 2: Use the parameter**

In the same method, change:

```csharp
            Type = session.Aspsp?.Name ?? "EnableBanking",
```

to:

```csharp
            Type = bankName,
```

- [ ] **Step 3: Drop the now-unused using**

Remove line 3 of the file:

```csharp
using EnableBanking.Models.Sessions;
```

`GetSessionResponse` was its only use in this file. Leaving it produces no error but the file should stay clean.

- [ ] **Step 4: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: the `EnableBankingService.cs` errors from Task 3, plus one more at its line 67 for the changed overload. Both are fixed in Task 8.

- [ ] **Step 5: Commit**

```bash
git add BS.Logic/Workbook/ExpenseService.cs
git commit -m "refactor: pass bank name to CreateExpense instead of the session"
```

---

### Task 6: Session loading and metadata self-heal

**Files:**
- Modify: `BS.Logic/EnableBanking/EnableBankingService.cs:1-33` (usings, constructor, new private helpers)

**Test level:** Manual verification in Task 10 Step 1, because the self-heal path only exercises against the real legacy session file and live API.

- [ ] **Step 1: Replace the usings and constructor**

Replace lines 1-21 of `BS.Logic/EnableBanking/EnableBankingService.cs` with:

```csharp
using BS.Data;
using BS.Logic.Workbook;
using EnableBanking.Config;
using EnableBanking.Interfaces;
using EnableBanking.Models.Accounts;
using EnableBanking.Models.General;
using EnableBanking.Models.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Access = EnableBanking.Models.General.Access;
using Aspsp = EnableBanking.Models.General.Aspsp;

namespace EnableBanking;

public class EnableBankingService(
    IGeneralService enableGeneralService,
    ISessionsService enableSessionsService,
    IAccountsService enableAccountService,
    ExpenseService expenseService,
    SessionKeyStore sessionKeyStore,
    IOptions<EnableBankingSettings> settings,
    IConfiguration configuration,
    ILogger<EnableBankingService> logger)
{
    private readonly EnableBankingSettings _settings = settings.Value;
```

Keep the existing `ValidateConnection` method (lines 23-33) exactly as it is.

- [ ] **Step 2: Add the loading helpers**

Insert these three private methods directly after `ValidateConnection`:

```csharp
    /// <summary>
    /// Reads stored sessions and fills in any missing metadata from the API.
    /// With <paramref name="verifyAll"/> false, complete records are trusted as-is so a routine
    /// sync makes no session calls at all. With it true, every record is re-checked against the
    /// API and dead ones are dropped.
    /// </summary>
    private async Task<List<BankSession>> LoadSessionsAsync(bool verifyAll)
    {
        var stored = sessionKeyStore.GetSessions();
        var live = new List<BankSession>();
        var changed = false;

        foreach (var record in stored)
        {
            if (!verifyAll && !record.IsIncomplete)
            {
                live.Add(record);
                continue;
            }

            var session = await enableSessionsService.GetSessionAsync(
                new GetSessionRequest { SessionId = record.SessionId }, CancellationToken.None);

            if (session.Error != null || session.Data == null)
            {
                logger.LogWarning("Dropping session {SessionId}: {Message}",
                    record.SessionId, session.Error?.Message ?? "no data returned");
                changed = true;
                continue;
            }

            if (await FillMetadataAsync(record, session.Data))
            {
                changed = true;
            }

            if (record.ValidUntil != session.Data.Access?.ValidUntil)
            {
                record.ValidUntil = session.Data.Access?.ValidUntil;
                changed = true;
            }

            live.Add(record);
        }

        if (changed)
        {
            sessionKeyStore.SaveSessions(live);
        }

        return live;
    }

    /// <summary>Resolves bank and account details for a record that lacks them. Returns true if it changed.</summary>
    private async Task<bool> FillMetadataAsync(BankSession record, GetSessionResponse session)
    {
        if (!record.IsIncomplete)
        {
            return false;
        }

        record.Bank = session.Aspsp?.Name ?? string.Empty;
        record.Country = session.Aspsp?.Country ?? string.Empty;
        record.Accounts = [];

        foreach (var uid in session.Accounts ?? [])
        {
            var details = await enableAccountService.GetDetailsAsync(
                new GetDetailsRequest { AccountId = uid }, CancellationToken.None);

            if (details.Error != null || details.Data == null)
            {
                logger.LogWarning("Session {SessionId}: could not resolve account {Uid}: {Message}",
                    record.SessionId, uid, details.Error?.Message ?? "no data returned");
                continue;
            }

            var iban = EnableBankingSettings.NormalizeIban(
                details.Data.AccountId?.Iban
                ?? details.Data.AllAccountIds?.FirstOrDefault(id => id.SchemeName == "IBAN")?.Identification);

            if (iban.Length == 0)
            {
                continue;
            }

            record.Accounts.Add(new StoredAccount { Uid = uid, Iban = iban });
        }

        logger.LogInformation("Resolved session {SessionId} as {Bank} with {Count} account(s)",
            record.SessionId, record.Bank, record.Accounts.Count);

        return true;
    }

    /// <summary>Builds a store record from a freshly authorized session.</summary>
    private static BankSession ToRecord(AuthorizeSessionResponse response, BankSettings bank) => new()
    {
        SessionId = response.SessionId ?? Guid.Empty,
        Bank = bank.Name,
        Country = bank.Country,
        ValidUntil = response.Access?.ValidUntil,
        Accounts = (response.Accounts ?? [])
            .Select(account => new StoredAccount
            {
                Uid = account.Uid ?? Guid.Empty,
                Iban = EnableBankingSettings.NormalizeIban(
                    account.AccountId?.Iban
                    ?? account.AllAccountIds?.FirstOrDefault(id => id.Iban != null)?.Iban)
            })
            .Where(account => account.Uid != Guid.Empty && account.Iban.Length > 0)
            .ToList()
    };
```

Two different `AllAccountIds` types are in play and they are easy to confuse:
`GetDetailsResponse.AllAccountIds` is `AllAccountId[]` with `Identification`/`SchemeName`, while
`AuthorizeSessionResponse.Account.AllAccountIds` is `AccountId[]` with `Iban`. The code above uses each correctly.

- [ ] **Step 3: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: still errors in the not-yet-rewritten `CreateNewAccountCheck` and `GetEnableTransactions` bodies. No errors in the new helpers.

- [ ] **Step 4: Commit**

```bash
git add BS.Logic/EnableBanking/EnableBankingService.cs
git commit -m "feat: add session loading with metadata self-heal"
```

---

### Task 7: ConnectAsync

**Files:**
- Modify: `BS.Logic/EnableBanking/EnableBankingService.cs:77-194` (replace `CreateNewAccountCheck`)

**Test level:** Manual verification in Task 10 Steps 1-2, because the flow is inherently interactive and hits the live Enable Banking authorization endpoint.

- [ ] **Step 1: Replace CreateNewAccountCheck entirely**

Delete the whole `CreateNewAccountCheck` method and put this in its place:

```csharp
    /// <summary>
    /// Interactive. Ensures every configured bank has one session covering all its configured IBANs.
    /// Invoked by `BankSync Connect`, never by a routine sync.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_settings.Banks.Count == 0)
        {
            logger.LogWarning("No banks configured under EnableBanking:Banks; nothing to connect.");
            return;
        }

        if (!await ValidateConnection())
        {
            return;
        }

        var sessions = await LoadSessionsAsync(verifyAll: true);

        var cutoff = DateTime.UtcNow.AddDays(_settings.RenewBeforeDays);
        var live = new List<BankSession>();
        foreach (var session in sessions)
        {
            if (session.ValidUntil.HasValue && session.ValidUntil.Value < cutoff)
            {
                logger.LogInformation("{Bank}: session {SessionId} expires {ValidUntil}; it will be replaced.",
                    session.Bank, session.SessionId, session.ValidUntil);
                continue;
            }

            live.Add(session);
        }

        sessionKeyStore.SaveSessions(live);

        foreach (var bank in _settings.Banks)
        {
            var existing = live.FirstOrDefault(session =>
                string.Equals(session.Bank, bank.Name, StringComparison.OrdinalIgnoreCase));

            var covered = existing?.Accounts
                .Select(account => account.Iban)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

            var missing = bank.Ibans.Where(iban => !covered.Contains(iban)).ToList();

            if (missing.Count == 0)
            {
                logger.LogInformation("{Bank}: all {Count} configured account(s) already covered by session {SessionId}.",
                    bank.Name, bank.Ibans.Count, existing!.SessionId);
                continue;
            }

            await AuthorizeBankAsync(bank, existing);
        }
    }

    private async Task AuthorizeBankAsync(BankSettings bank, BankSession? existing)
    {
        var validityDays = bank.ConsentValidityDays ?? _settings.ConsentValidityDays;

        var authorization = await enableGeneralService.StartAuthorizationAsync(new StartAuthorizationRequest
        {
            Access = new Access
            {
                ValidUntil = DateTime.UtcNow.AddDays(validityDays),
                Balances = true,
                Transactions = true,
                Accounts = bank.Ibans.Select(iban => new Models.General.Account { Iban = iban }).ToArray()
            },
            CredentialsAutosubmit = true,
            RedirectUrl = _settings.RedirectUrl,
            State = Guid.NewGuid().ToString(),
            PsuType = bank.PsuType,
            Aspsp = new Aspsp { Name = bank.Name, Country = bank.Country }
        }, CancellationToken.None);

        if (authorization.Error != null || authorization.Data?.Url == null)
        {
            logger.LogError("{Bank}: could not start authorization: {Message}",
                bank.Name, authorization.Error?.Message ?? "no URL returned");
            return;
        }

        logger.LogInformation("{Bank}: open this URL, authorize {Count} account(s), then paste the code from the redirect:\n{Url}",
            bank.Name, bank.Ibans.Count, authorization.Data.Url);

        Console.Write($"{bank.Name} code: ");
        var code = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(code))
        {
            logger.LogWarning("{Bank}: no code entered, skipping.", bank.Name);
            return;
        }

        var authorized = await enableSessionsService.AuthorizeSessionAsync(
            new AuthorizeSessionRequest { Code = code.Trim() }, CancellationToken.None);

        if (authorized.Error != null || authorized.Data?.SessionId == null)
        {
            logger.LogError("{Bank}: authorization failed: {Message}",
                bank.Name, authorized.Error?.Message ?? "no session id returned");
            return;
        }

        var record = ToRecord(authorized.Data, bank);
        sessionKeyStore.AddOrReplace(record);

        logger.LogInformation("{Bank}: session {SessionId} created covering {Count} account(s).",
            bank.Name, record.SessionId, record.Accounts.Count);

        var uncovered = bank.Ibans
            .Where(iban => record.Accounts.All(account =>
                !string.Equals(account.Iban, iban, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (uncovered.Count > 0)
        {
            logger.LogWarning("{Bank}: the new session does not cover {Ibans}. Check the IBANs in configuration.",
                bank.Name, string.Join(", ", uncovered));
        }

        if (existing == null)
        {
            return;
        }

        var deleted = await enableSessionsService.DeleteSessionAsync(
            new DeleteSessionRequest { SessionId = existing.SessionId }, CancellationToken.None);

        if (deleted.Error != null)
        {
            logger.LogWarning("{Bank}: could not delete superseded session {SessionId}: {Message}",
                bank.Name, existing.SessionId, deleted.Error.Message);
        }
    }
```

Deleting the old session happens only after the new one is saved, so a failed authorization never leaves a bank with no session. `AddOrReplace` already evicted the old record by bank name, so no extra store write is needed.

- [ ] **Step 2: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: only `GetEnableTransactions` errors remain (its `GetIds` call, `Accounts[0]` use, and the `CreateExpense` overload).

- [ ] **Step 3: Commit**

```bash
git add BS.Logic/EnableBanking/EnableBankingService.cs
git commit -m "feat: add ConnectAsync driving authorization from configured banks"
```

---

### Task 8: Config-driven transaction retrieval with working pagination

**Files:**
- Modify: `BS.Logic/EnableBanking/EnableBankingService.cs:35-75` (replace `GetEnableTransactions`)

**Test level:** Manual verification in Task 10 Step 3, because correctness depends on live paged API responses and on values landing in the workbook.

- [ ] **Step 1: Replace GetEnableTransactions entirely**

Delete the existing method and put this in its place:

```csharp
    public async Task<List<Expense>> GetEnableTransactions()
    {
        if (_settings.Banks.Count == 0)
        {
            logger.LogInformation("No banks configured under EnableBanking:Banks; skipping Enable Banking.");
            return [];
        }

        if (!await ValidateConnection())
        {
            return [];
        }

        var sessions = await LoadSessionsAsync(verifyAll: false);
        var retrievalDays = int.Parse(configuration["RetrievalDays"] ?? "31");
        var dateFrom = DateTime.UtcNow.AddDays(-retrievalDays);
        var expenses = new List<Expense>();

        foreach (var bank in _settings.Banks)
        {
            var session = sessions.FirstOrDefault(stored =>
                string.Equals(stored.Bank, bank.Name, StringComparison.OrdinalIgnoreCase));

            if (session == null)
            {
                logger.LogWarning("{Bank}: no stored session. Run `BankSync Connect`.", bank.Name);
                continue;
            }

            if (session.ValidUntil.HasValue && session.ValidUntil.Value < DateTime.UtcNow)
            {
                logger.LogWarning("{Bank}: session expired on {ValidUntil}. Run `BankSync Connect`.",
                    bank.Name, session.ValidUntil);
                continue;
            }

            foreach (var iban in bank.Ibans)
            {
                var account = session.Accounts.FirstOrDefault(stored =>
                    string.Equals(stored.Iban, iban, StringComparison.OrdinalIgnoreCase));

                if (account == null)
                {
                    logger.LogWarning("{Bank}: session {SessionId} does not cover {Iban}. Run `BankSync Connect`.",
                        bank.Name, session.SessionId, iban);
                    continue;
                }

                expenses.AddRange(await GetAccountTransactionsAsync(bank.Name, iban, account.Uid, dateFrom));
            }
        }

        logger.LogInformation("Enable Banking returned {Count} transaction(s) across {Banks} bank(s)",
            expenses.Count, _settings.Banks.Count);

        return expenses;
    }

    private async Task<List<Expense>> GetAccountTransactionsAsync(string bankName, string iban, Guid accountUid, DateTime dateFrom)
    {
        var expenses = new List<Expense>();
        string? continuationKey = null;

        do
        {
            var page = await enableAccountService.GetTransactionsAsync(new GetTransactionsRequest
            {
                AccountId = accountUid,
                DateFrom = dateFrom,
                ContinuationKey = continuationKey
            }, CancellationToken.None);

            if (page.Error != null)
            {
                logger.LogError("{Bank}/{Iban}: could not fetch transactions: {Message}",
                    bankName, iban, page.Error.Message);
                break;
            }

            expenses.AddRange(page.Data?.Transactions?.Select(transaction =>
                expenseService.CreateExpense(transaction, bankName)) ?? []);

            continuationKey = page.Data?.ContinuationKey;
        } while (!string.IsNullOrEmpty(continuationKey));

        logger.LogInformation("{Bank}/{Iban}: {Count} transaction(s)", bankName, iban, expenses.Count);

        return expenses;
    }
```

`continuationKey` is now assigned from the response and fed into the next request, and the loop exits on null or empty. Together with Task 4 this makes pagination work rather than truncating at one page.

- [ ] **Step 2: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: `0 Error(s)`. `Application.cs` still calls `CreateNewAccountCheck`, which no longer exists — if that errors, it is fixed in Task 9. Complete Task 9 before judging the build clean.

- [ ] **Step 3: Commit**

```bash
git add BS.Logic/EnableBanking/EnableBankingService.cs
git commit -m "feat: drive transaction retrieval from configured banks with working pagination"
```

---

### Task 9: Wire up the Connect command

**Files:**
- Modify: `BS.Logic/Application.cs:26-27`
- Modify: `BS.Console/Program.cs:67-75`

**Test level:** Manual verification in Task 10 Step 4, because the risk being proven is that an unattended sync no longer blocks on stdin — only a real run shows that.

- [ ] **Step 1: Stop the sync path calling the interactive flow**

In `BS.Logic/Application.cs`, delete these two lines (26-27):

```csharp
        // await goCardlessService.CreateNewAccCheck();
        await enableBankingService.CreateNewAccountCheck();
```

`enableBankingService` stays a constructor parameter — `GetEnableTransactions` still uses it on line 39.

- [ ] **Step 2: Add the Connect branch**

In `BS.Console/Program.cs`, replace the closing block (lines 67-75):

```csharp
if (args.Contains("GenerateTrainingData"))
{
    var trainingDataService = host.Services.GetRequiredService<TrainingDataService>();
    trainingDataService.GenerateTrainingData();
}
else
{
    await app.Run();
}
```

with:

```csharp
if (args.Contains("Connect"))
{
    var enableBankingService = host.Services.GetRequiredService<EnableBankingService>();
    await enableBankingService.ConnectAsync();
}
else if (args.Contains("GenerateTrainingData"))
{
    var trainingDataService = host.Services.GetRequiredService<TrainingDataService>();
    trainingDataService.GenerateTrainingData();
}
else
{
    await app.Run();
}
```

`using EnableBanking;` is already present at line 10.

- [ ] **Step 3: Build**

```bash
dotnet build BankSync.sln -v q --nologo
```

Expected: `0 Error(s)`. Warning count should be at or below the 44 baseline.

- [ ] **Step 4: Commit**

```bash
git add BS.Logic/Application.cs BS.Console/Program.cs
git commit -m "feat: move Enable Banking authorization behind the Connect command"
```

---

### Task 10: End-to-end manual verification

**Files:** None modified — this task validates the whole change.

**Test level:** Manual E2E against the live Enable Banking API, because every remaining risk (legacy migration, real pagination, multi-bank consent) depends on live responses and the on-disk session file.

**Before starting:** back up the existing session file so a failed migration is recoverable.

```bash
cp "C:/Users/kobe.wulteputte/Desktop/sessionkeys.json" "C:/Users/kobe.wulteputte/Desktop/sessionkeys.backup.json"
```

- [ ] **Step 1: Legacy migration and self-heal**

With only Revolut configured (the Task 2 shape), run:

```bash
dotnet run --project BS.Console -- Connect
```

Expected:
- Log lines `Resolved session <id> as Revolut with N account(s)`.
- `sessionkeys.json` is now a JSON array with `sessionId`, `bank`, `country`, `accounts` (uid + iban) and `validUntil`.
- `Revolut: all 2 configured account(s) already covered by session <id>.`
- **No authorization prompt.** A prompt here means the stored IBANs did not match config — compare the `iban` values written to the file against `EnableBanking:Banks[0].Ibans`.

- [ ] **Step 2: Add a second bank**

Add a real second bank to `EnableBanking:Banks` in `appsettings.json`, for example:

```json
      {
        "Name": "KBC",
        "Country": "BE",
        "Ibans": [ "<your KBC IBAN>" ]
      }
```

The `Name` must match Enable Banking's ASPSP name exactly. If authorization fails with an unknown-ASPSP error, list the available names by calling `IGeneralService.GetASPSPsAsync` or checking the Enable Banking control panel.

Run:

```bash
dotnet run --project BS.Console -- Connect
```

Expected:
- Revolut reports already covered and is **not** re-prompted.
- KBC prints an authorization URL and waits at `KBC code: `.
- After pasting the code: `KBC: session <id> created covering 1 account(s).`
- `sessionkeys.json` now holds two records.

- [ ] **Step 3: Full sync**

```bash
dotnet run --project BS.Console
```

Expected:
- No authorization prompt and no blocking on stdin.
- Per-account lines `Revolut/BE29650184652964: N transaction(s)` and the KBC equivalent.
- `Enable Banking returned N transaction(s) across 2 bank(s)`.
- In `Expenses.xlsx`, the `Type` column reads `Revolut` for Revolut rows and `KBC` for KBC rows.

- [ ] **Step 4: Missing session degrades gracefully**

Remove the KBC record from `sessionkeys.json` by hand, then run:

```bash
dotnet run --project BS.Console
```

Expected:
- `KBC: no stored session. Run `BankSync Connect`.`
- Revolut still syncs.
- The process exits normally without waiting on input.

Restore the KBC record afterwards, or re-run `Connect`.

- [ ] **Step 5: Commit any fixes**

If steps 1-4 surfaced fixes, commit them:

```bash
git add -A
git commit -m "fix: address issues found in multi-bank end-to-end verification"
```

---

## Verification summary

| Spec requirement | Task |
| --- | --- |
| Bank list configurable in appsettings | 1, 2 |
| Per-bank consent validity override | 1, 7 |
| Startup validation and IBAN normalization | 1, 2 |
| JSON session store with bank/IBAN/UID metadata | 3 |
| Legacy session file self-heals | 3, 6, 10.1 |
| One session per bank, delete-after-authorize | 7 |
| Separate Connect command | 9, 10.2 |
| Sync never blocks on stdin | 9, 10.4 |
| Only configured IBANs sync | 8 |
| Pagination fixed on both sides | 4, 8 |
| Expense.Type carries the bank name | 5, 10.3 |
| Per-bank failures do not abort the run | 7, 8, 10.4 |
