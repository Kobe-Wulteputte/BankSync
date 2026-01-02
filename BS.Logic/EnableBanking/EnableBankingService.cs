using BS.Data;
using BS.Logic.Workbook;
using EnableBanking.Interfaces;
using EnableBanking.Models.Accounts;
using EnableBanking.Models.General;
using EnableBanking.Models.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Access = EnableBanking.Models.General.Access;
using Aspsp = EnableBanking.Models.General.Aspsp;

namespace EnableBanking;

public class EnableBankingService(
    IGeneralService enableGeneralService,
    ISessionsService enableSessionsService,
    IAccountsService enableAccountService,
    ExpenseService expenseService,
    SessionKeyStore sessionKeyStore,
    IConfiguration configuration,
    ILogger<EnableBankingService> logger)
{
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
        //


        return expenses;
    }

    public async Task CreateNewAccountCheck()
    {
        if (!await ValidateConnection())
        {
            return;
        }

        // Cleanup old sessions
        var sessionKeys = sessionKeyStore.GetIds();
        foreach (var sessionKey in sessionKeys)
        {
            var session = await enableSessionsService.GetSessionAsync(new GetSessionRequest()
            {
                SessionId = Guid.Parse(sessionKey)
            }, CancellationToken.None);
            if (session.Error != null)
            {
                logger.LogError($"Error fetching session {sessionKey}: {session.Error.Message}");
                sessionKeyStore.RemoveId(sessionKey);
                continue;
            }

            if (session.Data?.Access?.ValidUntil != null && session.Data.Access.ValidUntil < DateTime.UtcNow.AddDays(1))
            {
                sessionKeyStore.RemoveId(sessionKey);
            }
        }
        
        var requiredIbans = new List<string>()
        {
            "BE29650184652964",
            "BE50650280329118"
        };
        var missingIbans = requiredIbans.ToList();
        sessionKeys = sessionKeyStore.GetIds();

        foreach (var iban in requiredIbans)
        {
            bool hasSession = false;
            foreach (var sessionKey in sessionKeys)
            {
                var session = await enableSessionsService.GetSessionAsync(new GetSessionRequest()
                {
                    SessionId = Guid.Parse(sessionKey)
                }, CancellationToken.None);
                if (session.Error != null)
                {
                    continue;
                }

                var account = await enableAccountService.GetDetailsAsync(new GetDetailsRequest()
                {
                    AccountId = session.Data.Accounts[0]
                }, CancellationToken.None);
                if (account.Error != null)
                {
                    continue;
                }

                if (account.Data.AllAccountIds.FirstOrDefault(acId => acId.Identification == iban) != null)
                {
                    logger.LogInformation($"Found existing session for IBAN {iban} with session key {sessionKey}");
                    hasSession = true;
                    break;
                }
            }

            if (hasSession)
            {
                missingIbans.Remove(iban);
            }
        }

        foreach (var missingIban in missingIbans)
        {
            var authresult = await enableGeneralService.StartAuthorizationAsync(new StartAuthorizationRequest()
            {
                Access = new Access()
                {
                    ValidUntil = DateTime.UtcNow.AddDays(90),
                    Balances = true,
                    Transactions = true,
                    Accounts =
                    [
                        new Models.General.Account()
                        {
                            Iban = missingIban
                        }
                    ],
                },
                CredentialsAutosubmit = true,
                RedirectUrl = new Uri("https://localhost:8080"),
                State = Guid.NewGuid().ToString(),
                PsuType = "personal",
                Aspsp = new Aspsp()
                {
                    Name = "Revolut",
                    Country = "BE"
                }
            }, CancellationToken.None);

            logger.LogInformation("To authenticate open URL: {DataUrl}", authresult.Data.Url);

            var code = Console.ReadLine();

            var authsessions = await enableSessionsService.AuthorizeSessionAsync(new AuthorizeSessionRequest()
            {
                Code = code
            }, CancellationToken.None);
            if (authsessions.Error != null)
            {
                logger.LogError($"Error authorizing session for IBAN {missingIban}: {authsessions.Error.Message}");
                continue;
            }
            sessionKeyStore.AddId(authsessions.Data.SessionId.ToString());
            logger.LogInformation($"Successfully created session for IBAN {missingIban} with session key {authsessions.Data.SessionId}");
        }
    }
}