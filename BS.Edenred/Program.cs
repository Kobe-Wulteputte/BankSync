using Betalgo.Ranul.OpenAI.Extensions;
using BS.Data;
using BS.Edenred;
using BS.Edenred.Api;
using BS.Edenred.Auth;
using BS.Edenred.Config;
using BS.Logic.CategoryGuesser;
using BS.Logic.Workbook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Offline test mode: map a saved operations JSON to Expense rows and print them.
// No browser, no Excel, no OpenAI. Usage: dotnet run -- --dry-run <path-to-operations.json>
if (args is ["--dry-run", var jsonPath, ..])
{
    var json = File.ReadAllText(jsonPath);
    var parsed = System.Text.Json.JsonSerializer.Deserialize<EdenredOperationsResponse>(
        json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    var expenseService = new ExpenseService();
    Console.WriteLine("Type    | Amount   | Date       | Name                         | Description                          | Id");
    foreach (var op in parsed?.Data ?? [])
    {
        var e = expenseService.CreateExpense(op);
        Console.WriteLine($"{e.Type,-7} | {e.Amount,8:0.00} | {e.Date:yyyy-MM-dd} | {Trunc(e.Name, 28),-28} | {Trunc(e.Description, 36),-36} | {e.Id}");
    }
    return;

    static string Trunc(string? s, int n) => (s ?? "").Length <= n ? (s ?? "") : (s ?? "")[..(n - 1)] + "…";
}

IHost host = Host.CreateDefaultBuilder(args)
    // See BS.Console: anchored to the app directory so the working directory cannot break config
    // loading, and so appsettings.{Environment}.json is picked up.
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;
        var edenredSettings = new EdenredSettings
        {
            LoginUrl = config["Edenred:LoginUrl"] ?? "https://myedenred.be",
            ApiBaseUrl = config["Edenred:ApiBaseUrl"] ?? "https://api.be.edenred.io/xp-user/v1.0/",
            LoginTimeoutSeconds = int.TryParse(config["Edenred:LoginTimeoutSeconds"], out var lt) ? lt : 300,
            PostLoginCaptureSeconds = int.TryParse(config["Edenred:PostLoginCaptureSeconds"], out var pc) ? pc : 90,
            PostLoginSettleSeconds = int.TryParse(config["Edenred:PostLoginSettleSeconds"], out var ps) ? ps : 4
        };
        services.AddSingleton(edenredSettings);

        services.AddTransient<BrowserTokenCapturer>();
        services.AddTransient<EdenredApiClient>();
        services.AddTransient<ExpenseService>();
        services.AddTransient<WorkbookService>();
        services.AddTransient<AiCategoryGuesserService>();
        services.AddTransient<EdenredImporter>();

        services.AddOpenAIService();
    })
    .Build();

var importer = host.Services.GetRequiredService<EdenredImporter>();
await importer.Run();
