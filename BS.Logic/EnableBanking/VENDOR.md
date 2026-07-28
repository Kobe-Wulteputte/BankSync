# Vendored: EnableBanking .NET client

The code in this folder is a vendored copy of a third-party client, not original to BankSync.

| | |
| --- | --- |
| Upstream | https://github.com/tech-gian/EnableBanking |
| Author | Ioannis Zapantis |
| Licence | MIT |
| Package id | `EnableBanking` (v1.0.0 at the time it was copied in) |

It arrived as a full project (`EnableBanking.csproj`) dropped inside `BS.Logic`. That project was
never added to `BankSync.sln` and nothing referenced it — because SDK-style projects glob `**/*.cs`,
these sources were being compiled straight into the `BS.Logic` assembly, which is why
`using EnableBanking;` resolves with no project reference. The redundant project file was removed;
the sources still compile into `BS.Logic` exactly as they did before.

The upstream `LICENSE.txt` and `README.md` were referenced by that project file but never actually
copied into this repository. This file exists so the origin and licence are not lost.

## Local modifications

These files diverge from upstream:

- `Models/Accounts/GetTransactionsResponse.cs` — `ContinuationKey` was tagged
  `[JsonProperty("continuationKey")]`, but the API returns `continuation_key`. The key never bound,
  so transaction retrieval silently stopped after the first page.
- `Services/HttpClientService.cs` — request serialization now ignores nulls. Several request models
  reuse the fat response types (`StartAuthorizationRequest.Aspsp` is the ASPSP *listing* type), so
  the defaults posted nine null metadata fields into `POST /auth`.
- `Handlers/PsuHeaderHandler.cs` — added. Sends `psu-ip-address`, which some ASPSPs list in their
  `required_psu_headers` metadata (Argenta does; Revolut does not).
- `Handlers/TokenHandlerOptions.cs` — gained `PsuIpAddress`.
- `ConfigureServices.cs` — registers the handler above on every client.
- `Config/EnableBankingSettings.cs` — added; BankSync-specific configuration, not upstream code.
- `EnableBankingService.cs` — added; BankSync-specific orchestration, not upstream code.

Upstream fixes will not arrive automatically. If this is ever re-pulled, re-apply the first three
items above.
