using NodaTime;
using VMelnalksnis.NordigenDotNet.DependencyInjection;
using BS.Console;
using BS.Logic;

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
        services.AddNordigenDotNet(ctx.Configuration);
    }).ConfigureLogging((context, cfg) =>
    {
        cfg.ClearProviders();
        cfg.AddConfiguration(context.Configuration.GetSection("Logging"));
        cfg.AddConsole();
    })
    .Build();

var app = host.Services.GetRequiredService<Application>();
await app.Run();
Console.ReadLine();