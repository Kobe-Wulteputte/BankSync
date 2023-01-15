using BS.Logic;
using VMelnalksnis.NordigenDotNet;

namespace BS.Console;

public class Application
{
    private readonly ILogger<Application> _logger;
    private readonly InstitutionService _institutionService;
    private readonly RequisitionService _requisitionService;

    public Application(ILogger<Application> logger, InstitutionService institutionService, RequisitionService requisitionService)
    {
        _logger = logger;
        _institutionService = institutionService;
        _requisitionService = requisitionService;
    }

    public async Task Run()
    {
        // Haal de requisitions binnen die geldig zijn
        // Check bij het EUA of het nog lang geldig is, zoniet melding sturen voor 
        // Overloop alle accounts, dubbels te skippen
        // Verwerk alle accounts

        // Vorm data om juiste formaat

        // Haal al opgeslagen transacties weg

        var inst = await _institutionService.GetAll();
        var reqs = _requisitionService.GetAll();
        await foreach (var req in reqs)
        {
            _logger.LogInformation(req.ToString());
        }

        // var account = await _nordigenClient.Accounts.Get(requisitions.Accounts[1]);
        // var transactions = await _nordigenClient.Accounts.GetTransactions(account.Id);

        _logger.LogInformation("Hello World!");
    }
}