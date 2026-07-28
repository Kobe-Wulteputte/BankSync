using System.Net;
using System.Net.Mail;
using Betalgo.Ranul.OpenAI.Extensions;
using BS.Data;
using BS.Logic;
using BS.Logic.CategoryGuesser;
using BS.Logic.Mailing;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using EnableBanking;
using EnableBanking.Config;
using Microsoft.Extensions.Options;
using NodaTime;
using Serilog;
using VMelnalksnis.NordigenDotNet.DependencyInjection;

IHost host = Host.CreateDefaultBuilder(args)
    // Pinned to the app directory so a scheduled task or service launch, which starts the process
    // in system32, still finds configuration. This also restores the default probing chain:
    // appsettings.json then appsettings.{Environment}.json, which loading one file by hand skipped.
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<Application, Application>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton(DateTimeZoneProviders.Tzdb);
        services.AddTransient<InstitutionService>();
        services.AddTransient<RequisitionService>();
        services.AddTransient<AccountService>();
        services.AddTransient<EndUserAgreementService>();
        services.AddTransient<WorkbookService>();
        services.AddTransient<ExpenseService>();
        services.AddTransient<TrainingDataService>();
        services.AddTransient<CategoryGuesserService>();
        services.AddTransient<CategoryLearnerService>();
        services.AddTransient<AiCategoryGuesserService>();
        services.AddTransient<MailSenderService>();
        services.AddTransient<GoCardlessService>();
        services.AddTransient<SessionKeyStore>();
        services.AddTransient<PendingAuthorizationStore>();
        var enableBankingSettings = new EnableBankingSettings();
        ctx.Configuration.GetSection("EnableBanking").Bind(enableBankingSettings);
        enableBankingSettings.NormalizeAndValidate();
        services.AddSingleton(Options.Create(enableBankingSettings));
        services.AddEnableBankingApi(options =>
        {
            options.KeyPath = enableBankingSettings.KeyPath;
            options.AppKid = enableBankingSettings.AppKid;
            options.PsuIpAddress = enableBankingSettings.PsuIpAddress;
        });
        services
            .AddFluentEmail(ctx.Configuration["Mail:From"], "BankSync")
            .AddSmtpSender(new SmtpClient()
                {
                    Host = ctx.Configuration["Mail:SmtpHost"],
                    Port = int.Parse(ctx.Configuration["Mail:SmtpPort"] ?? "587"),
                    EnableSsl = true,
                    Credentials = new NetworkCredential(ctx.Configuration["Mail:SmtpUser"], ctx.Configuration["Mail:SmtpPassword"])
                }
            );
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


var app = host.Services.GetRequiredService<Application>();

if (args.Contains("Connect"))
{
    var enableBankingService = host.Services.GetRequiredService<EnableBankingService>();
    await enableBankingService.ConnectAsync();
}
else if (args.Contains("GenerateTrainingData"))
{
    var trainingDataService = host.Services.GetRequiredService<TrainingDataService>();
    trainingDataService.GenerateTrainingData();
}
else
{
    // Transaction retrieval is opt-in: the ASPSPs rate-limit it, so a debug run should only do the
    // session check and email any authorization links that are needed.
    await app.Run(loadTransactions: args.Contains("LoadTransactions"));
}
