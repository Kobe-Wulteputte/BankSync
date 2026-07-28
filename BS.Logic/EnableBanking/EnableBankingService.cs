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

            var identifier = ResolveIdentifier(
                details.Data.AccountId?.Iban
                ?? details.Data.AllAccountIds?.FirstOrDefault(id => id.SchemeName == "IBAN")?.Identification,
                details.Data.AccountId?.Other?.Identification
                ?? details.Data.AllAccountIds?.FirstOrDefault()?.Identification
                ?? details.Data.Name);

            if (identifier.Length == 0)
            {
                continue;
            }

            record.Accounts.Add(new StoredAccount { Uid = uid, Iban = identifier });
        }

        // If no account resolved (e.g. a closed account), Accounts stays empty and IsIncomplete
        // stays true, so this record is retried on every future sync. That's intentional: it lets
        // a transient resolution failure heal itself rather than being silently accepted as final.
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
                Iban = ResolveIdentifier(
                    account.AccountId?.Iban
                    ?? account.AllAccountIds?.FirstOrDefault(id => id.Iban != null)?.Iban,
                    account.AccountId?.Other?.Identification
                    ?? account.AllAccountIds?.FirstOrDefault(id => id.Other?.Identification != null)?.Other?.Identification
                    ?? account.Name)
            })
            .Where(account => account.Uid != Guid.Empty && account.Iban.Length > 0)
            .ToList()
    };

    /// <summary>
    /// Picks the identifier stored for an account. An IBAN is normalized so it can be compared
    /// against configured IBANs; anything else is kept verbatim as a label, because ASPSPs without
    /// IBANs (PayPal) identify accounts by things like an email address that normalization would
    /// mangle. Such accounts are only reachable via a bank configured to sync all its accounts.
    /// </summary>
    private static string ResolveIdentifier(string? iban, string? fallback)
    {
        var normalized = EnableBankingSettings.NormalizeIban(iban);

        return normalized.Length > 0 ? normalized : (fallback ?? string.Empty).Trim();
    }

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

        if (!int.TryParse(configuration["RetrievalDays"], out var retrievalDays))
        {
            retrievalDays = 31;
            logger.LogWarning("RetrievalDays is missing or not a number; defaulting to {Days} days.", retrievalDays);
        }

        var dateFrom = DateTime.UtcNow.AddDays(-retrievalDays);
        var expenses = new List<Expense>();
        var syncedBanks = 0;

        foreach (var bank in _settings.Banks)
        {
            var bankSessions = sessions
                .Where(stored => string.Equals(stored.Bank, bank.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (bankSessions.Count == 0)
            {
                logger.LogWarning("{Bank}: no stored session. Run `BankSync Connect`.", bank.Name);
                continue;
            }

            // Deliberately no renewal buffer here — a session is usable right up to its expiry.
            // The RenewBeforeDays buffer is a ConnectAsync concern, not a usability one.
            var usableSessions = bankSessions
                .Where(stored => !stored.ValidUntil.HasValue || stored.ValidUntil.Value >= DateTime.UtcNow)
                .ToList();

            if (usableSessions.Count == 0)
            {
                logger.LogWarning("{Bank}: all stored session(s) expired. Run `BankSync Connect`.", bank.Name);
                continue;
            }

            syncedBanks++;

            if (bank.SyncsAllAccounts)
            {
                var allAccounts = usableSessions.SelectMany(stored => stored.Accounts).ToList();

                logger.LogInformation("{Bank}: no IBANs configured, syncing all {Count} account(s) in the session.",
                    bank.Name, allAccounts.Count);

                foreach (var account in allAccounts)
                {
                    expenses.AddRange(await GetAccountTransactionsAsync(bank.Name, account.Iban, account.Uid, dateFrom));
                }

                continue;
            }

            foreach (var iban in bank.Ibans)
            {
                var account = usableSessions
                    .SelectMany(stored => stored.Accounts)
                    .FirstOrDefault(stored => string.Equals(stored.Iban, iban, StringComparison.OrdinalIgnoreCase));

                if (account == null)
                {
                    logger.LogWarning("{Bank}: no stored session covers {Iban}. Run `BankSync Connect`.", bank.Name, iban);
                    continue;
                }

                expenses.AddRange(await GetAccountTransactionsAsync(bank.Name, iban, account.Uid, dateFrom));
            }
        }

        logger.LogInformation("Enable Banking returned {Count} transaction(s) from {Synced}/{Total} bank(s), {Skipped} skipped",
            expenses.Count, syncedBanks, _settings.Banks.Count, _settings.Banks.Count - syncedBanks);

        return expenses;
    }

    private async Task<List<Expense>> GetAccountTransactionsAsync(string bankName, string iban, Guid accountUid, DateTime dateFrom)
    {
        var expenses = new List<Expense>();
        string? continuationKey = null;

        do
        {
            var previousKey = continuationKey;

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

            if (continuationKey != null && continuationKey == previousKey)
            {
                logger.LogError("{Bank}/{Iban}: continuation key did not advance; stopping pagination.", bankName, iban);
                break;
            }
        } while (!string.IsNullOrEmpty(continuationKey));

        logger.LogInformation("{Bank}/{Iban}: {Count} transaction(s)", bankName, iban, expenses.Count);

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

        // Deliberately uses a RenewBeforeDays buffer here (unlike GetEnableTransactions' bare
        // ValidUntil < UtcNow check): Connect is the proactive renewal path, so a session that is
        // still valid but expiring soon is replaced now rather than left to fail on some later sync.
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
            var existingForBank = live
                .Where(session => string.Equals(session.Bank, bank.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var covered = existingForBank
                .SelectMany(session => session.Accounts)
                .Select(account => account.Iban)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // With no IBANs configured there is nothing to compare, so a live session is enough.
            List<string> missing = bank.SyncsAllAccounts
                ? []
                : bank.Ibans.Where(iban => !covered.Contains(iban)).ToList();

            if (bank.SyncsAllAccounts && existingForBank.Count > 0)
            {
                logger.LogInformation("{Bank}: no IBANs configured; {Sessions} session(s) covering {Count} account(s) will all be synced.",
                    bank.Name, existingForBank.Count, covered.Count);
                continue;
            }

            if (missing.Count == 0 && existingForBank.Count > 0)
            {
                logger.LogInformation("{Bank}: all {Count} configured account(s) already covered by {Sessions} session(s).",
                    bank.Name, bank.Ibans.Count, existingForBank.Count);
                continue;
            }

            await AuthorizeBankAsync(bank, existingForBank);
        }
    }

    private async Task AuthorizeBankAsync(BankSettings bank, List<BankSession> superseded)
    {
        var validityDays = bank.ConsentValidityDays ?? _settings.ConsentValidityDays;

        var authorization = await enableGeneralService.StartAuthorizationAsync(new StartAuthorizationRequest
        {
            Access = new Access
            {
                ValidUntil = DateTime.UtcNow.AddDays(validityDays),
                Balances = true,
                Transactions = true,
                // Omitted when the ASPSP will not honour a pre-specified list (the user then picks
                // accounts in the bank's own screens), and when no IBANs are configured at all —
                // an empty array would request access to nothing.
                Accounts = bank.SelectAccountsAtBank || bank.SyncsAllAccounts
                    ? null
                    : bank.Ibans.Select(iban => new Models.General.Account { Iban = iban }).ToArray()
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

        if (bank.SelectAccountsAtBank || bank.SyncsAllAccounts)
        {
            logger.LogInformation("{Bank}: open this URL and select the account(s) you want to sync, then paste the code from the redirect:\n{Url}",
                bank.Name, authorization.Data.Url);
        }
        else
        {
            logger.LogInformation("{Bank}: open this URL, authorize {Count} account(s), then paste the code from the redirect:\n{Url}",
                bank.Name, bank.Ibans.Count, authorization.Data.Url);
        }

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

        foreach (var oldSession in superseded.Where(session => session.SessionId != record.SessionId))
        {
            var deleted = await enableSessionsService.DeleteSessionAsync(
                new DeleteSessionRequest { SessionId = oldSession.SessionId }, CancellationToken.None);

            if (deleted.Error != null)
            {
                logger.LogWarning("{Bank}: could not delete superseded session {SessionId}: {Message}",
                    bank.Name, oldSession.SessionId, deleted.Error.Message);
            }
        }
    }
}
