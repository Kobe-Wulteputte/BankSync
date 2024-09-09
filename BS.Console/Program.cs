using System.Net;
using System.Net.Mail;
using BS.Logic;
using BS.Logic.CategoryGuesser;
using BS.Logic.Mailing;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using FluentEmail.Smtp;
using NodaTime;
using OpenAI.Extensions;
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
        services.AddTransient<AiCategoryGuesserService, AiCategoryGuesserService>();
        services.AddTransient<MailSenderService, MailSenderService>();
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

// string[] arguments = Environment.GetCommandLineArgs();
//
// if (arguments.Length > 1)
// {
//     if (arguments[1] == "learn")
//     {
//         var learner = host.Services.GetRequiredService<CategoryLearnerService>();
//         learner.RunExpenseLearner();
//         return;
//     }
// }

var app = host.Services.GetRequiredService<Application>();
await app.CreateNewAccCheck();
await app.Run();