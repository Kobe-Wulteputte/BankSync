using System.Net;
using BS.Data;
using BS.Logic.CategoryGuesser;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using VMelnalksnis.NordigenDotNet.Accounts;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic;

public class Application
{
    private readonly AccountService _accountService;
    private readonly ExpenseService _expenseService;
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
}