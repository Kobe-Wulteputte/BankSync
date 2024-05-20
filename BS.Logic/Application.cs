using System.Net;
using BS.Data;
using BS.Logic.CategoryGuesser;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OpenAI.Interfaces;
using VMelnalksnis.NordigenDotNet.Accounts;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic;

public class Application
{
    private readonly AccountService _accountService;
    private readonly ExpenseService _expenseService;
    private readonly EndUserAgreementService _endUserAgreementService;
    private readonly AiCategoryGuesserService _categoryGuesser;
    private readonly IOpenAIService _openAiService;
    private readonly ILogger<Application> _logger;
    private readonly RequisitionService _requisitionService;
    private readonly IConfiguration _configuration;
    private readonly WorkbookService _workbookService;

    public Application(
        ILogger<Application> logger,
        RequisitionService requisitionService,
        IConfiguration configuration,
        ExpenseService expenseService,
        EndUserAgreementService endUserAgreementService,
        AiCategoryGuesserService categoryGuesser,
        IOpenAIService openAiService,
        WorkbookService workbookService,
        AccountService accountService
    )
    {
        _logger = logger;
        _requisitionService = requisitionService;
        _configuration = configuration;
        _expenseService = expenseService;
        _endUserAgreementService = endUserAgreementService;
        _categoryGuesser = categoryGuesser;
        _openAiService = openAiService;
        _workbookService = workbookService;
        _accountService = accountService;
    }

    public async Task Run()
    {
        var accounts = new HashSet<Guid>();
        var reqs = _requisitionService.GetAll();

        await foreach (Requisition req in reqs)
        {
            if (req.Status != RequisitionStatus.Ln)
            {
                _logger.LogWarning($"Skipping requisition with id: {req.Id}, {req.InstitutionId}");
                _logger.LogInformation($"Status: {req.Status}, Link: {req.Link}");
                continue;
            }

            // Handle other types later
            _logger.LogInformation($"Processing requisition with id: {req.Id}, {req.InstitutionId}");
            foreach (Guid reqAccount in req.Accounts) accounts.Add(reqAccount);
        }

        _logger.LogInformation($"Loaded {accounts.Count} accounts");

        var filePath = _configuration["FilePaths:Expenses"];
        _workbookService.OpenWorkBook(filePath);

        var transactions = new List<Expense>();
        foreach (Guid accountGuid in accounts)
        {
            Account account = await _accountService.Get(accountGuid);
            try
            {
                var accountTransactions = await _accountService.GetTransactions(accountGuid);
                if (accountTransactions.Count == 0)
                {
                    _logger.LogWarning($"No transactions for account {account.InstitutionId} {account.Iban}");
                    continue;
                }

                _logger.LogInformation($"Found {accountTransactions.Count} transactions for account {account.InstitutionId} {accountGuid}");
                transactions.AddRange(accountTransactions.Select(x => _expenseService.CreateExpense(x, account.InstitutionId)));
            }
            catch (HttpRequestException e)
            {
                _logger.LogError($"Error for account {account.InstitutionId} {account.Iban}");
                _logger.LogError(e.Message);
                if (e.StatusCode == HttpStatusCode.Conflict)
                {
                    await foreach (Requisition req in reqs)
                    {
                        if (req.Accounts.Contains(accountGuid))
                        {
                            _logger.LogWarning($"To enable account {account.InstitutionId} {account.Iban} please visit {req.Link}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error for account {account.InstitutionId} {account.Iban}");
                _logger.LogError(e.Message);
            }
        }

        _logger.LogInformation($"Found a total of {transactions.Count} transactions");
        transactions = _workbookService.RemoveDuplicates(transactions).ToList();
        _logger.LogInformation($"Found a total of {transactions.Count} new transactions");
        foreach (Expense transaction in transactions)
        {
            var category = await _categoryGuesser.Guess(transaction);
            if (category.HasValue)
            {
                _logger.LogInformation($"Transaction {transaction.Name} has category {category}");
                transaction.Category = JsonConvert.SerializeObject(category, new StringEnumConverter()).Replace("\"", "");
            }
            else
            {
                transaction.Category = "";
            }
        }


        var perYear = transactions.OrderBy(x => x.Date).GroupBy(x => x.Date.Value.Year);
        foreach (var grouping in perYear)
            _workbookService.WriteTransactions(grouping, _workbookService.GetWorksheet(grouping.Key.ToString()));

        _logger.LogInformation("Saving and closing workbook");
        _workbookService.SaveAndClose();

        _logger.LogInformation("Done");
    }

    public async Task Tst()
    {
        await AiFineTuneService.Test(_openAiService);
    }

    public async Task CreateNewAccCheck()
    {
        var reqs = _requisitionService.GetAll();
        await foreach (var requisition in reqs)
        {
            if (requisition.Status != RequisitionStatus.Ex) continue;
            _logger.LogInformation($"Deleting requisition {requisition.Id}");
            await _endUserAgreementService.TryDeleteEndUserAgreement(requisition.Agreement.Value);
            await _requisitionService.Delete(requisition.Id);

            _logger.LogInformation($"Creating new requisition and end user agreement for institution {requisition.InstitutionId}");
            var eua = await _endUserAgreementService.CreateEndUserAgreement(requisition.InstitutionId);
            await _requisitionService.New(requisition.InstitutionId, requisition.InstitutionId + $"{new DateTime():yyyy-MM-dd}", eua.Id);
        }
        // await _endUserAgreementService.DeleteAllEndUserAgreements();
        // await _requisitionService.DeleteAllRequisitions();
        // var eua1 = await _endUserAgreementService.CreateEndUserAgreement(InstitutionService.ArgentaInstitutionId);
        // var eua2 = await _endUserAgreementService.CreateEndUserAgreement(InstitutionService.PayPalInstitutionId);
        // var eua3 = await _endUserAgreementService.CreateEndUserAgreement(InstitutionService.RevolutInstitutionId);
        //
        // var req1 = await _requisitionService.New(InstitutionService.ArgentaInstitutionId, "Argenta2402", eua1.Id);
        // var req2 = await _requisitionService.New(InstitutionService.PayPalInstitutionId, "Paypal2402", eua2.Id);
        // var req3 = await _requisitionService.New(InstitutionService.RevolutInstitutionId, "Revolut2402",eua3.Id);
    }
}