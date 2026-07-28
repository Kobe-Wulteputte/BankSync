# BS.Edenred

Console app that imports **myEdenred.be (Belgium)** card transactions into the shared
`Expenses.xlsx`, reusing the same workbook writer and OpenAI category guesser as the bank sync.

## Flow

1. **Login (browser)** – Launches your real Google Chrome at `myedenred.be`. You log in yourself
   (email/password + itsme/MFA) and open your transactions page. The app captures the bearer token
   and `X-Client-Id`/`X-Client-Secret` headers from the web app's own calls to `api.be.edenred.io`.
   Credentials are typed by you; nothing is stored, and the token is never written to disk.
2. **Fetch** – Calls `GET accounts/accountRef/operations` (`accountRef` is a literal path segment;
   the account is resolved from the token).
3. **Map** – Each operation → `Expense` via `ExpenseService.CreateExpense(EdenredOperation)`:
   `Type="Edenred"`, amount in € (debit −, credit +), `Name`=merchant, `Id="EDENRED-<operation_ref>"`.
4. **Categorize + write** – Dedupes by `Id` against the workbook, guesses a category with
   `AiCategoryGuesserService`, and appends rows to the per-year sheet(s). Both spend and top-ups are
   included.

## Prerequisites

- .NET 8 SDK
- Playwright's Chromium (one-time, shared across the machine):
  `pwsh bin/Debug/net8.0/playwright.ps1 install chromium`
- `OpenAIServiceOptions` and `FilePaths:Expenses` configured in `appsettings.json` (the OpenAI key is
  read from the shared user-secrets store — same `UserSecretsId` as `BS.Console`).

## Run

```bash
dotnet run --project BS.Edenred
```

### Offline test (no login / no Excel)

Map a previously saved operations JSON to Expense rows and print them:

```bash
dotnet run --project BS.Edenred -- --dry-run path/to/operations.json
```

## Config (`appsettings.json`)

| Key | Meaning |
| --- | --- |
| `FilePaths:Expenses` | Path to the shared expenses workbook |
| `OpenAIServiceOptions` | OpenAI key/model for the category guesser |
| `Edenred:LoginUrl` | Entry URL (`https://myedenred.be`) |
| `Edenred:ApiBaseUrl` | XP API base for the fetch |
| `Edenred:LoginTimeoutSeconds` / `PostLoginCaptureSeconds` / `PostLoginSettleSeconds` | Capture timing |

Unofficial API — may break if Edenred changes their frontend. Personal use, your own account only.
