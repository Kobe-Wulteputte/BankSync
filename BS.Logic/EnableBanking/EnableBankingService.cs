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

    private async Task<bool> ValidateConnection()
    {
        var apps = await enableGeneralService.GetApplicationAsync(new GetApplicationRequest(), CancellationToken.None);
        if (apps.Error != null)
        {
            logger.LogError($"Error fetching applications: {apps.Error.Message}");
            return false;
        }

        return true;
    }

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

    public async Task<List<Expense>> GetEnableTransactions()
    {
        if (!await ValidateConnection())
        {
            return [];
        }

        var expenses = new List<Expense>();
        var sessionKeys = sessionKeyStore.GetIds();
        logger.LogInformation($"Found {sessionKeys.Count} stored session keys");
        foreach (var sessionKey in sessionKeys)
        {
            logger.LogInformation($"Using session key: {sessionKey}");
            var session = await enableSessionsService.GetSessionAsync(new GetSessionRequest()
            {
                SessionId = Guid.Parse(sessionKey)
            }, CancellationToken.None);
            if (session.Error != null)
            {
                logger.LogError($"Error fetching session {sessionKey}: {session.Error.Message}");
                continue;
            }


            string? continueationKey = null;
            do
            {
                var sessionTransactions = await enableAccountService.GetTransactionsAsync(new GetTransactionsRequest()
                {
                    AccountId = session.Data.Accounts[0],
                    DateFrom = DateTime.UtcNow.AddDays(-1 * int.Parse(configuration["RetrievalDays"])),
                }, CancellationToken.None);
                expenses.AddRange(sessionTransactions.Data?.Transactions?.Select(t => expenseService.CreateExpense(t, session.Data)) ?? []);
                continueationKey = sessionTransactions.Data?.ContinuationKey;
            } while (continueationKey != null);
        }



        return expenses;
    }

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
}
