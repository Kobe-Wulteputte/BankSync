using System.Net;
using BS.Data;
using BS.Logic.Workbook;
using EnableBanking;
using EnableBanking.Config;
using Microsoft.Extensions.Options;

// The content root is pinned to the app directory rather than the working directory. IIS starts
// worker processes in system32, so the default probing would look for configuration there and find
// nothing. Setting it here means the normal chain works: appsettings.json, then
// appsettings.{Environment}.json, then user secrets, then environment variables — each overriding
// the last. Loading a single file by hand instead is what previously caused
// appsettings.Production.json to be ignored.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Added last, and unconditionally rather than only in Development, so real values override the
// placeholders committed in appsettings.json even when IIS runs this as Production.
builder.Configuration.AddUserSecrets<Program>(optional: true);

var enableBankingSettings = new EnableBankingSettings();
builder.Configuration.GetSection("EnableBanking").Bind(enableBankingSettings);
enableBankingSettings.NormalizeAndValidate();

builder.Services.AddSingleton(Options.Create(enableBankingSettings));
builder.Services.AddTransient<SessionKeyStore>();
builder.Services.AddTransient<PendingAuthorizationStore>();
builder.Services.AddTransient<ExpenseService>();
builder.Services.AddEnableBankingApi(options =>
{
    options.KeyPath = enableBankingSettings.KeyPath;
    options.AppKid = enableBankingSettings.AppKid;
    options.PsuIpAddress = enableBankingSettings.PsuIpAddress;
});

// Bound to loopback on the exact port the Enable Banking application registers as its redirect.
// This endpoint completes bank authorizations, so it has no business being reachable from
// anywhere but this machine. HTTPS needs a trusted local certificate:
//   dotnet dev-certs https --trust
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(
    IPAddress.Loopback,
    enableBankingSettings.RedirectUrl.Port,
    listen => listen.UseHttps()));

var app = builder.Build();

app.MapGet("/", async (
    HttpContext context,
    EnableBankingService enableBankingService,
    ILogger<Program> logger) =>
{
    var code = context.Request.Query["code"].ToString();
    var state = context.Request.Query["state"].ToString();
    var error = context.Request.Query["error"].ToString();

    if (!string.IsNullOrWhiteSpace(error))
    {
        logger.LogWarning("Callback reported an error: {Error}", error);
        return Results.Content(Page("Authorization failed", error), "text/html");
    }

    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(state))
    {
        // Someone opened the root URL directly rather than arriving from a bank.
        return Results.Content(Page("BankSync", "Waiting for a bank authorization callback."), "text/html");
    }

    var result = await enableBankingService.CompleteAuthorizationAsync(code, state);

    return Results.Content(
        Page(result.Success ? "Authorized" : "Authorization failed", result.Message),
        "text/html");
});

app.Run();

static string Page(string heading, string message) =>
    $"""
     <!doctype html>
     <html lang="en">
     <head><meta charset="utf-8"><title>BankSync</title></head>
     <body style="font-family:system-ui,sans-serif;max-width:36rem;margin:4rem auto;padding:0 1rem">
       <h1 style="font-size:1.25rem">{WebUtility.HtmlEncode(heading)}</h1>
       <p>{WebUtility.HtmlEncode(message)}</p>
     </body>
     </html>
     """;