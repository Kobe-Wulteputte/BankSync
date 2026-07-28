using System.Net;
using BS.Data;
using BS.Logic.CategoryGuesser;
using BS.Logic.Mailing;
using BS.Logic.Nordigen;
using BS.Logic.Workbook;
using EnableBanking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BS.Logic;

public class Application(
    ILogger<Application> logger,
    IConfiguration configuration,
    AiCategoryGuesserService categoryGuesser,
    WorkbookService workbookService,
    EnableBankingService enableBankingService,
    MailSenderService mailSenderService,
    GoCardlessService goCardlessService)
{
    /// <summary>
    /// Session checking runs every time; transaction retrieval is opt-in because the ASPSPs
    /// rate-limit it and a debug run should not spend that budget.
    /// </summary>
    public async Task Run(bool loadTransactions)
    {
        logger.LogInformation("Starting application");

        await CheckSessionsAsync();

        if (!loadTransactions)
        {
            logger.LogInformation("Skipping transaction retrieval; pass LoadTransactions to include it.");
            return;
        }

        try
        {
            var filePath = configuration["FilePaths:Expenses"];
            var loaded = workbookService.OpenWorkBook(filePath);
            if (!loaded)
            {
                logger.LogError("Could not open workbook");
                return;
            }

            var enableTransactions = await enableBankingService.GetEnableTransactions();
            // var goCardlessTransactions = await goCardlessService.GetGoCardlessTransactions();

            var transactions = enableTransactions;
            // transactions.AddRange(goCardlessTransactions);


            logger.LogInformation($"Found a total of {transactions.Count} transactions");
            transactions = workbookService.RemoveDuplicates(transactions).ToList();
            logger.LogInformation($"Found a total of {transactions.Count} new transactions");
            foreach (Expense transaction in transactions)
            {
                var category = await categoryGuesser.Guess(transaction);
                if (category.HasValue)
                {
                    logger.LogInformation($"Transaction {transaction.Name} - {transaction.Description} has category {category}");
                    transaction.Category = JsonConvert.SerializeObject(category, new StringEnumConverter()).Replace("\"", "");
                }
                else
                {
                    transaction.Category = "";
                }
            }


            var perYear = transactions.OrderBy(x => x.Date).GroupBy(x => x.Date.Year);
            foreach (var grouping in perYear)
                workbookService.WriteTransactions(grouping, workbookService.GetWorksheet(grouping.Key.ToString()));

            logger.LogInformation("Saving and closing workbook");
            workbookService.SaveAndClose();

            logger.LogInformation("Done");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error in application");
            throw;
        }
    }

    /// <summary>
    /// Emails an authorization link for every bank that needs one. Never blocks: the link is
    /// completed later by the callback API, which is why this can run unattended.
    /// </summary>
    private async Task CheckSessionsAsync()
    {
        List<PendingAuthorization> pending;
        try
        {
            pending = await enableBankingService.PrepareAuthorizationsAsync();
        }
        catch (Exception e)
        {
            // A failure here must not stop a transaction run that could otherwise succeed.
            logger.LogError(e, "Could not check Enable Banking sessions");
            return;
        }

        if (pending.Count == 0)
        {
            logger.LogInformation("All configured banks have a usable session.");
            return;
        }

        var recipient = configuration["Mail:To"];
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogError("{Count} bank(s) need authorization but Mail:To is not configured. Links: {Urls}",
                pending.Count, string.Join(" | ", pending.Select(p => $"{p.Bank}: {p.Url}")));
            return;
        }

        foreach (var authorization in pending)
        {
            try
            {
                await mailSenderService.SendMail(
                    $"BankSync: authorize {authorization.Bank}",
                    BuildAuthorizationEmail(authorization),
                    recipient,
                    isHtml: true,
                    plainTextAlternative: BuildAuthorizationEmailText(authorization));

                logger.LogInformation("{Bank}: authorization link emailed to {Recipient}.", authorization.Bank, recipient);
            }
            catch (Exception e)
            {
                // The link is already stored as pending, so a send failure would otherwise go unnoticed
                // until it went stale. Log the URL so the run is still actionable.
                logger.LogError(e, "{Bank}: could not email the authorization link. Open it manually: {Url}",
                    authorization.Bank, authorization.Url);
            }
        }
    }

    /// <summary>
    /// Table-based layout with inline styles throughout, because mail clients strip stylesheets and
    /// several still have no support for modern layout. Everything is HTML-encoded: bank names and
    /// the URL come from configuration and an external API.
    /// </summary>
    private static string BuildAuthorizationEmail(PendingAuthorization authorization)
    {
        var bank = WebUtility.HtmlEncode(authorization.Bank);
        var country = WebUtility.HtmlEncode(authorization.Country);
        var url = WebUtility.HtmlEncode(authorization.Url);

        return $"""
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f4f5f7;margin:0;padding:24px 0;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:520px;background:#ffffff;border:1px solid #e2e5e9;border-radius:8px;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
                        <tr>
                          <td style="padding:24px 28px 0 28px;">
                            <div style="font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:#8a9099;">BankSync</div>
                            <div style="margin-top:6px;font-size:20px;line-height:1.3;font-weight:600;color:#1b1f24;">Authorize {bank}</div>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:14px 28px 0 28px;font-size:15px;line-height:1.55;color:#3c4149;">
                            Your {bank} ({country}) connection needs re-authorization before transactions can sync again.
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:22px 28px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                              <tr>
                                <td style="border-radius:6px;background:#1f6feb;">
                                  <a href="{url}" style="display:inline-block;padding:11px 22px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;">Authorize {bank}</a>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:0 28px 24px 28px;font-size:13px;line-height:1.55;color:#6b7280;border-top:1px solid #eef0f2;padding-top:16px;">
                            Start the BankSync callback service before opening this link, otherwise the redirect has nowhere to land.
                            The link stops working after a while; if it does, the next BankSync run sends a fresh one.
                            <div style="margin-top:12px;font-size:12px;color:#9aa0a6;word-break:break-all;">{url}</div>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
                """;
    }

    /// <summary>Alternative body for clients that do not render HTML.</summary>
    private static string BuildAuthorizationEmailText(PendingAuthorization authorization) =>
        $"""
         BankSync

         Your {authorization.Bank} ({authorization.Country}) connection needs re-authorization
         before transactions can sync again.

         Authorize here:
         {authorization.Url}

         Start the BankSync callback service before opening this link, otherwise the redirect has
         nowhere to land. The link stops working after a while; if it does, the next BankSync run
         sends a fresh one.
         """;
}