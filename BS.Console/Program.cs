using BS.Logic;
using BS.Logic.CategoryGuesser;
using BS.Logic.FileRetrieval;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using NodaTime;
using OpenAI.GPT3.Extensions;
using Serilog;
using VMelnalksnis.NordigenDotNet.DependencyInjection;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureHostConfiguration(cfg => { cfg.AddJsonFile("appsettings.json"); })
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<Application, Application>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton(DateTimeZoneProviders.Tzdb);
        services.AddTransient<InstitutionService, InstitutionService>();
        services.AddTransient<RequisitionService, RequisitionService>();
        services.AddTransient<AccountService, AccountService>();
        services.AddTransient<EndUserAgreementService, EndUserAgreementService>();
        services.AddTransient<WorkbookService, WorkbookService>();
        services.AddTransient<ExpenseService, ExpenseService>();
        services.AddTransient<CategoryGuesserService, CategoryGuesserService>();
        services.AddTransient<CategoryLearnerService, CategoryLearnerService>();
        services.AddTransient<GraphService, GraphService>();
        services.AddTransient<AiCategoryGuesserService, AiCategoryGuesserService>();
        services.AddNordigenDotNet(ctx.Configuration);
        services.AddOpenAIService();
    }).ConfigureLogging((context, cfg) =>
    {
        cfg.ClearProviders();
        cfg.AddSerilog(new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(context.Configuration["FilePaths:logs"] ?? "log.txt", rollingInterval: RollingInterval.Month)
            .ReadFrom.Configuration(context.Configuration)
            .CreateLogger()
        );
    })
    .Build();

string[] arguments = Environment.GetCommandLineArgs();

if (arguments.Length > 1)
{
    if (arguments[1] == "learn")
    {
        var learner = host.Services.GetRequiredService<CategoryLearnerService>();
        learner.RunExpenseLearner();
        return;
    }
}

var app = host.Services.GetRequiredService<Application>();
await app.Run();