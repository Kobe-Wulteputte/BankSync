using BS.Data;
using BS.Logic;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Console;

public class Application
{
    private readonly ILogger<Application> _logger;
    private readonly InstitutionService _institutionService;
    private readonly RequisitionService _requisitionService;
    private readonly ExpenseService _expenseService;
    private readonly WorkbookService _workbookService;
    private readonly AccountService _accountService;

    public Application(
        ILogger<Application> logger,
        InstitutionService institutionService,
        RequisitionService requisitionService,
        ExpenseService expenseService,
        WorkbookService workbookService,
        AccountService accountService
    )
    {
        _logger = logger;
        _institutionService = institutionService;
        _requisitionService = requisitionService;
        _expenseService = expenseService;
        _workbookService = workbookService;
        _accountService = accountService;
    }

    public async Task Run()
    {
        var accounts = new HashSet<Guid>();
        var reqs = _requisitionService.GetAll();

        await foreach (var req in reqs)
        {
            if (req.Status != RequisitionStatus.Ln) continue;
            // Handle other types later
            _logger.LogInformation($"Processing requisition with id: {req.Id}");
            foreach (Guid reqAccount in req.Accounts)
            {
                accounts.Add(reqAccount);
            }
        }

        _logger.LogInformation($"Loaded {accounts.Count} accounts");

        _workbookService.OpenWorkBook();
        var transactions = new List<Expense>();
        foreach (Guid account in accounts)
        {
            var accountTransactions = await _accountService.GetTransactions(account);
            if (accountTransactions.Count == 0)
            {
                _logger.LogWarning($"No transactions for account {account}");
                continue;
            }

            _logger.LogInformation($"Found {accountTransactions.Count} transactions for account {account}");
            transactions.AddRange(accountTransactions.Select(x => _expenseService.CreateExpense(x)));
        }

        transactions = transactions.OrderBy(x => x.Date).ToList();
        _workbookService.WriteTransactions(transactions);

        _workbookService.SaveAndClose();
    }
}