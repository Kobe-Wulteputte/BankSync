using BS.Data;
using BS.Logic.CategoryGuesser;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using VMelnalksnis.NordigenDotNet.Accounts;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Logic;

public class Application
{
    private readonly AccountService _accountService;
    private readonly ExpenseService _expenseService;
    private readonly CategoryGuesserService _categoryGuesser;
    private readonly InstitutionService _institutionService;
    private readonly ILogger<Application> _logger;
    private readonly RequisitionService _requisitionService;
    private readonly WorkbookService _workbookService;

    public Application(
        ILogger<Application> logger,
        InstitutionService institutionService,
        RequisitionService requisitionService,
        ExpenseService expenseService,
        CategoryGuesserService categoryGuesser,
        WorkbookService workbookService,
        AccountService accountService
    )
    {
        _logger = logger;
        _institutionService = institutionService;
        _requisitionService = requisitionService;
        _expenseService = expenseService;
        _categoryGuesser = categoryGuesser;
        _workbookService = workbookService;
        _accountService = accountService;
    }

    public async Task Run()
    {
        var accounts = new HashSet<Guid>();
        var reqs = _requisitionService.GetAll();

        await foreach (Requisition req in reqs)
        {
            if (req.Status != RequisitionStatus.Ln) continue;
            // Handle other types later
            _logger.LogInformation($"Processing requisition with id: {req.Id}");
            foreach (Guid reqAccount in req.Accounts) accounts.Add(reqAccount);
        }

        _logger.LogInformation($"Loaded {accounts.Count} accounts");

        _workbookService.OpenWorkBook("C:/Users/kwlt/Desktop/ExpenseTest.xlsx");

        var transactions = new List<Expense>();
        foreach (Guid accountGuid in accounts)
        {
            var accountTransactions = await _accountService.GetTransactions(accountGuid);
            Account account = await _accountService.Get(accountGuid);
            if (accountTransactions.Count == 0)
            {
                _logger.LogWarning($"No transactions for account {account.InstitutionId} {account.Iban}");
                continue;
            }

            _logger.LogInformation($"Found {accountTransactions.Count} transactions for account {accountGuid}");
            transactions.AddRange(accountTransactions.Select(x => _expenseService.CreateExpense(x, account.InstitutionId)));
        }

        transactions = _workbookService.RemoveDuplicates(transactions).ToList();

        foreach (Expense transaction in transactions)
        {
            var category = _categoryGuesser.Guess(transaction);
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
        foreach (var grouping in perYear) _workbookService.WriteTransactions(grouping, _workbookService.GetWorksheet(grouping.Key.ToString()));

        _workbookService.SaveAndClose();
    }
}