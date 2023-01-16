using System.Net.Mime;
using System.Reflection;
using BS.Logic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodaTime;
using VMelnalksnis.NordigenDotNet.DependencyInjection;

var host = new HostBuilder()
    .ConfigureAppConfiguration((hostContext, builder) =>
    {
        builder
            .AddJsonFile("local.settings.json", true)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), false);
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton(DateTimeZoneProviders.Tzdb);
        services.AddTransient<InstitutionService, InstitutionService>();
        services.AddTransient<RequisitionService, RequisitionService>();
        services.AddTransient<AccountService, AccountService>();
        services.AddTransient<EndUserAgreementService, EndUserAgreementService>();
        services.AddTransient<WorkbookService, WorkbookService>();
        services.AddTransient<ExpenseService, ExpenseService>();
        services.AddNordigenDotNet(ctx.Configuration);
    })
    .Build();

host.Run();