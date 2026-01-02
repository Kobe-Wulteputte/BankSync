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
using OpenAI.Interfaces;

namespace BS.Logic;

public class Application(
    ILogger<Application> logger,
    IConfiguration configuration,
    AiCategoryGuesserService categoryGuesser,
    WorkbookService workbookService,
    EnableBankingService enableBankingService,
    GoCardlessService goCardlessService)
{
    public async Task Run()
    {
        logger.LogInformation("Starting application");

        await enableBankingService.CreateNewAccountCheck();

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
            var goCardlessTransactions = await goCardlessService.GetGoCardlessTransactions();

            var transactions = goCardlessTransactions;
            transactions.AddRange(enableTransactions);


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
}