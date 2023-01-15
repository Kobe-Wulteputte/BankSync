using BS.Logic;
using VMelnalksnis.NordigenDotNet.Requisitions;

namespace BS.Console;

public class Application
{
    private readonly ILogger<Application> _logger;
    private readonly InstitutionService _institutionService;
    private readonly RequisitionService _requisitionService;
    private readonly AccountService _accountService;

    public Application(
        ILogger<Application> logger,
        InstitutionService institutionService,
        RequisitionService requisitionService,
        AccountService accountService
    )
    {
        _logger = logger;
        _institutionService = institutionService;
        _requisitionService = requisitionService;
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

        foreach (Guid account in accounts)
        {
            var transactions = await _accountService.GetTransactions(account);
            if (transactions.Count == 0)
            {
                _logger.LogInformation($"No transactions for account {account}");
                continue;
            }

            _logger.LogCritical(transactions[0].ToString());
        }
    }
}