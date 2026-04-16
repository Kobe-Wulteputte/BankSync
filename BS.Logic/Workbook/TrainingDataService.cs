using System.Text.Json;
using BS.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BS.Logic.Workbook;

public class TrainingDataService(
    ILogger<TrainingDataService> logger,
    IConfiguration configuration,
    WorkbookService workbookService)
{
    public void GenerateTrainingData()
    {
        var expensesFilePath = configuration["FilePaths:Expenses"]
                               ?? throw new InvalidOperationException("FilePaths:Expenses is not configured");
        var trainingOutputPath = configuration["FilePaths:TrainingData"] ?? "training_data_{date}.jsonl";
        var validationOutputPath = configuration["FilePaths:ValidationData"] ?? "validation_data_{date}.jsonl";
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        trainingOutputPath = trainingOutputPath.Replace("{date}", date);
        validationOutputPath = validationOutputPath.Replace("{date}", date);

        logger.LogInformation("Opening workbook at {FilePath}", expensesFilePath);
        var loaded = workbookService.OpenWorkBook(expensesFilePath);
        if (!loaded)
        {
            logger.LogError("Could not open workbook");
            return;
        }

        var expenses = workbookService.GetAllExpenses()
            .Where(e => !string.IsNullOrWhiteSpace(e.Category))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var validationCount = Math.Max(1, (int) Math.Round(expenses.Count * 0.1));
        var validationExpenses = expenses.Take(validationCount).ToList();
        var trainingExpenses = expenses.Skip(validationCount).ToList();

        logger.LogInformation(
            "Splitting {Total} expenses into {Training} training and {Validation} validation records",
            expenses.Count, trainingExpenses.Count, validationExpenses.Count);

        WriteJsonl(trainingExpenses, trainingOutputPath);
        WriteJsonl(validationExpenses, validationOutputPath);

        workbookService.SaveAndClose();
        logger.LogInformation("Training data written to {TrainingPath}", trainingOutputPath);
        logger.LogInformation("Validation data written to {ValidationPath}", validationOutputPath);
    }

    private static void WriteJsonl(IEnumerable<Expense> expenses, string outputPath)
    {
        using var writer = new StreamWriter(outputPath, append: false);
        var categoryList = string.Join(", ", Enum.GetNames<CategoryEnum>());
        var systemMessage = $"You categorize bank transactions. " +
                            $"Reply with exactly one of these categories: {categoryList}.";
        foreach (var expense in expenses)
        {
            var record = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemMessage },
                    new { role = "user", content = expense.AiPrompt },
                    new { role = "assistant", content = $"{expense.Category}" }
                }
            };
            writer.WriteLine(JsonSerializer.Serialize(record));
        }
    }
}