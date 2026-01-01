using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BS.Data;
using BS.Logic.CategoryGuesser;
using BS.Logic.Mailing;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using EnableBanking.Interfaces;
using EnableBanking.Models.Accounts;
using EnableBanking.Models.General;
using EnableBanking.Models.Sessions;
using FluentEmail.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OpenAI.Interfaces;
using VMelnalksnis.NordigenDotNet.Requisitions;
using Access = EnableBanking.Models.General.Access;
using Account = VMelnalksnis.NordigenDotNet.Accounts.Account;
using Aspsp = EnableBanking.Models.General.Aspsp;
using JsonElement = System.Text.Json.JsonElement;

namespace BS.Logic;

public class Application(
    ILogger<Application> logger,
    RequisitionService requisitionService,
    IConfiguration configuration,
    ExpenseService expenseService,
    EndUserAgreementService endUserAgreementService,
    AiCategoryGuesserService categoryGuesser,
    IOpenAIService openAiService,
    SessionKeyStore sessionKeyStore,
    WorkbookService workbookService,
    MailSenderService mailSenderService,
    IGeneralService enableGeneralService,
    IAccountsService enableAccountService,
    ISessionsService enableSessionsService,
    AccountService accountService)
{
    public async Task Run()
    {
        logger.LogInformation("Starting application");


        try
        {
            var enableTransactions = await GetEnableTransactions();
            var goCardlessTransactions = await GetGoCardlessTransactions();

            var transactions = goCardlessTransactions;
            transactions.AddRange(enableTransactions);


            logger.LogInformation($"Found a total of {transactions.Count} transactions");
            transactions = workbookService.RemoveDuplicates(transactions).ToList();
            logger.LogInformation($"Found a total of {transactions.Count} new transactions");
            foreach (Expense transaction in transactions)
            {
                var category = await categoryGuesser.Guess(transaction);
                if (category.HasValue)
                {
                    logger.LogInformation($"Transaction {transaction.Name} has category {category}");
                    transaction.Category = JsonConvert.SerializeObject(category, new StringEnumConverter()).Replace("\"", "");
                }
                else
                {
                    transaction.Category = "";
                }
            }


            var perYear = transactions.OrderBy(x => x.Date).GroupBy(x => x.Date.Value.Year);
            foreach (var grouping in perYear)
                workbookService.WriteTransactions(grouping, workbookService.GetWorksheet(grouping.Key.ToString()));

            logger.LogInformation("Saving and closing workbook");
            workbookService.SaveAndClose();

            logger.LogInformation("Done");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error in application");
            throw;
        }
    }

    private async Task<List<Expense>> GetEnableTransactions()
    {
        var apps = await enableGeneralService.GetApplicationAsync(new GetApplicationRequest(), CancellationToken.None);
        if (apps.Error != null)
        {
            logger.LogError($"Error fetching applications: {apps.Error.Message}");
            return [];
        }

        var expesses = new List<Expense>();
        var sessionKeys = sessionKeyStore.GetIds();
        logger.LogInformation($"Found {sessionKeys.Count} stored session keys");
        foreach (var sessionKey in sessionKeys)
        {
            logger.LogInformation($"Using session key: {sessionKey}");
            var session = await enableSessionsService.GetSessionAsync(new GetSessionRequest()
            {
                SessionId = Guid.Parse(sessionKey)
            }, CancellationToken.None);
            
            // var account = await enableAccountService.GetDetailsAsync(new GetDetailsRequest()
            // {
            //     AccountId = session.Data.Accounts[0]
            // }, CancellationToken.None);

            string? continueationKey = null;
            do
            {
                var sessionTransactions = await enableAccountService.GetTransactionsAsync(new GetTransactionsRequest()
                {
                    AccountId = session.Data.Accounts[0],
                    DateFrom = DateTime.UtcNow.AddDays(-1 * int.Parse(configuration["RetrievalDays"])),
                }, CancellationToken.None);
                expesses.AddRange(sessionTransactions.Data?.Transactions?.Select(expenseService.CreateExpense) ?? []);
                continueationKey = sessionTransactions.Data?.ContinuationKey;
            } while (continueationKey != null);
        }
        //
        // var authresult = await enableGeneralService.StartAuthorizationAsync(new StartAuthorizationRequest()
        // {
        //     Access = new Access()
        //     {
        //         ValidUntil = DateTime.UtcNow.AddDays(10),
        //         Balances = true,
        //         Transactions = true,
        //         Accounts =
        //         [
        //             new EnableBanking.Models.General.Account()
        //             {
        //                 Iban = "BE29650184652964"
        //             }
        //         ],
        //     },
        //     CredentialsAutosubmit = true,
        //     RedirectUrl = new Uri("https://localhost:8080"),
        //     State = Guid.NewGuid().ToString(),
        //     PsuType = "personal",
        //     Aspsp = new Aspsp()
        //     {
        //         Name = "Revolut",
        //         Country = "BE"
        //     }
        // }, CancellationToken.None);
        //
        // logger.LogInformation("To authenticate open URL: {DataUrl}", authresult.Data.Url);

        // var code = Console.ReadLine();
        //
        // var authsessions = await enableSessionsService.AuthorizeSessionAsync(new AuthorizeSessionRequest()
        // {
        //     Code = code
        // }, CancellationToken.None);

        return expesses;
    }

    private async Task<List<Expense>> GetGoCardlessTransactions()
    {
        var accounts = new HashSet<Guid>();
        var reqs = requisitionService.GetAll();

        await foreach (Requisition req in reqs)
        {
            if (req.Status != RequisitionStatus.Ln)
            {
                logger.LogWarning($"Skipping requisition with id: {req.Id}, {req.InstitutionId}");
                logger.LogInformation($"Status: {req.Status}, Link: {req.Link}");
                if (req.Status == RequisitionStatus.Cr)
                {
                    await mailSenderService.SendMail("Banksync authorisation required",
                        $"Requisition {req.Id} is in status {req.Status}, please check {req.Link}", configuration["Mail:To"]);
                }

                continue;
            }

            // Handle other types later
            logger.LogInformation($"Processing requisition with id: {req.Id}, {req.InstitutionId}");
            foreach (Guid reqAccount in req.Accounts) accounts.Add(reqAccount);
        }

        logger.LogInformation($"Loaded {accounts.Count} accounts");

        var filePath = configuration["FilePaths:Expenses"];
        workbookService.OpenWorkBook(filePath);

        var transactions = new List<Expense>();
        foreach (Guid accountGuid in accounts)
        {
            Account account = await accountService.Get(accountGuid);
            try
            {
                var accountTransactions = await accountService.GetTransactions(accountGuid);
                if (accountTransactions.Count == 0)
                {
                    logger.LogWarning($"No transactions for account {account.InstitutionId} {account.Iban}");
                    continue;
                }

                logger.LogInformation($"Found {accountTransactions.Count} transactions for account {account.InstitutionId} {accountGuid}");
                transactions.AddRange(accountTransactions.Select(x => expenseService.CreateExpense(x, account.InstitutionId)));
            }
            catch (HttpRequestException e)
            {
                logger.LogError($"Error for account {account.InstitutionId} {account.Iban}");
                logger.LogError(e.Message);
                if (e.StatusCode == HttpStatusCode.Conflict)
                {
                    await foreach (Requisition req in reqs)
                    {
                        if (req.Accounts.Contains(accountGuid))
                        {
                            logger.LogWarning($"To enable account {account.InstitutionId} {account.Iban} please visit {req.Link}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError($"Error for account {account.InstitutionId} {account.Iban}");
                logger.LogError(e.Message);
            }
        }

        return transactions;
    }


    public async Task CreateNewAccCheck()
    {
        var reqs = requisitionService.GetAll();
        await foreach (var requisition in reqs)
        {
            if (requisition.Status != RequisitionStatus.Ex) continue;
            logger.LogInformation($"Deleting requisition {requisition.Id}");
            await endUserAgreementService.TryDeleteEndUserAgreement(requisition.Agreement.Value);
            await requisitionService.Delete(requisition.Id);

            logger.LogInformation($"Creating new requisition and end user agreement for institution {requisition.InstitutionId}");
            var eua = await endUserAgreementService.CreateEndUserAgreement(requisition.InstitutionId);
            await requisitionService.New(requisition.InstitutionId, requisition.InstitutionId + $"{DateTime.Now:yyyy-MM-dd}", eua.Id);
        }

        // await _endUserAgreementService.DeleteAllEndUserAgreements();
        // await _requisitionService.DeleteAllRequisitions();
        // var eua1 = await _endUserAgreementService.CreateEndUserAgreement(InstitutionService.ArgentaInstitutionId);
        // var eua2 = await _endUserAgreementService.CreateEndUserAgreement(InstitutionService.PayPalInstitutionId);
        // var eua3 = await endUserAgreementService.CreateEndUserAgreement(InstitutionService.RevolutInstitutionId);
        //
        // var req1 = await _requisitionService.New(InstitutionService.ArgentaInstitutionId, "Argenta2402", eua1.Id);
        // var req2 = await _requisitionService.New(InstitutionService.PayPalInstitutionId, "Paypal2402", eua2.Id);
        // var req3 = await requisitionService.New(InstitutionService.RevolutInstitutionId, "Revolut2501", eua3.Id);
    }
}