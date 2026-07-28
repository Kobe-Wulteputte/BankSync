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
            var body =
                $"""
                 <p>BankSync needs you to re-authorize <strong>{authorization.Bank}</strong> ({authorization.Country}).</p>
                 <p><a href="{authorization.Url}">Authorize {authorization.Bank}</a></p>
                 <p>Make sure the BankSync callback API is running before you open the link, otherwise the
                 redirect has nowhere to land. This link stops working after a while; if it fails, the next
                 BankSync run will send a fresh one.</p>
                 <p style="color:#888">{authorization.Url}</p>
                 """;

            try
            {
                await mailSenderService.SendMail($"BankSync: authorize {authorization.Bank}", body, recipient);
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
}