using System.Net;
using BS.Data;
using BS.Logic.Mailing;
using BS.Logic.Workbook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VMelnalksnis.NordigenDotNet.Accounts;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic.Nordigen;

public class GoCardlessService(
    ILogger<GoCardlessService> logger,
    RequisitionService requisitionService,
    IConfiguration configuration,
    ExpenseService expenseService,
    WorkbookService workbookService,
    AccountService accountService,
    EndUserAgreementService endUserAgreementService,
    MailSenderService mailSenderService)
{
    public async Task<List<Expense>> GetGoCardlessTransactions()
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