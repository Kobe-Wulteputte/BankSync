using System.Text.RegularExpressions;
using BS.Data;
using BS.Logic.Workbook;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BS.Logic.CategoryGuesser;

public class CategoryLearnerService
{
    private readonly WorkbookService _workbookService;
    private readonly ILogger<CategoryLearnerService> _logger;

    public CategoryLearnerService(WorkbookService workbookService, ILogger<CategoryLearnerService> logger)
    {
        _workbookService = workbookService;
        _logger = logger;
    }

    public void RunExpenseLearner()
    {
        // Laad de workbook in via workbookService
        _workbookService.OpenWorkBook("C:/Users/kwlt/Desktop/Expenses.xlsx");
        var expenses = _workbookService.GetAllExpenses().ToList();

        // Stel de dictionaries op
        var (incomeAccounts, expenseAccounts) = CreateAccountsDictionary(expenses);
        var descriptions = CreateDescriptions(expenses);
        // Schrijf de dictionaries weg naar file
        string jsonIncome = JsonConvert.SerializeObject(incomeAccounts, Formatting.Indented);
        File.WriteAllText(@"C:/Users/kwlt/Desktop/incomes.json", jsonIncome);
        string jsonExpense = JsonConvert.SerializeObject(expenseAccounts, Formatting.Indented);
        File.WriteAllText(@"C:/Users/kwlt/Desktop/expenses.json", jsonExpense);
        string jsonDescriptions = JsonConvert.SerializeObject(descriptions, Formatting.Indented);
        File.WriteAllText(@"C:/Users/kwlt/Desktop/descriptions.json", jsonDescriptions);
    }

    private (Dictionary<string, AccountClassification>, Dictionary<string, AccountClassification>) CreateAccountsDictionary(
        IEnumerable<Expense> expenses)
    {
        var incomeAccounts = new Dictionary<string, AccountClassification>();
        var expenseAccounts = new Dictionary<string, AccountClassification>();
        foreach (Expense expense in expenses
                     .Where(expense => expense.Name != ""))
        {
            FillExpensesDict(expense, expense.Amount > 0 ? incomeAccounts : expenseAccounts);
        }

        return (incomeAccounts, expenseAccounts);
    }

    private void FillExpensesDict(Expense expense, IDictionary<string, AccountClassification> accountClassificationDict)
    {
        if (!accountClassificationDict.TryGetValue(expense.Name, out var accountClassification))
        {
            accountClassification = new AccountClassification
            {
                Count = 0,
                CategoryAmounts = new Dictionary<CategoryEnum, int>()
            };
        }

        accountClassification.Count++;

        var category = ToEnum<CategoryEnum>(expense.Category);
        if (category.HasValue)
        {
            accountClassification.CategoryAmounts.TryGetValue(category.Value, out var categoryAmount);
            categoryAmount++;
            accountClassification.CategoryAmounts[category.Value] = categoryAmount;
        }

        accountClassificationDict[expense.Name] = accountClassification;
    }

    private IEnumerable<DescriptionClassification> CreateDescriptions(IEnumerable<Expense> expenses)
    {
        return expenses
            .Where(x => x.Description != "")
            .Where(x => x.Category != "")
            .Where(x => !Regex.Match(x.Description, @".*P2P MOBILE.*").Success)
            .Where(x => !Regex.Match(x.Description, @".*\+\+\+.*").Success)
            .Select(x =>
            {
                x.Description = Regex.Replace(x.Description, @"\d{2}-\d{2}-\d{4}", "");
                return x;
            }).Select(x =>
            {
                x.Description = Regex.Replace(x.Description, @"\d{2}:\d{2}", "");
                return x;
            }).Select(x =>
            {
                x.Description = Regex.Replace(x.Description, @"\w{2} \d{6}\*{7}\d{4}", "");
                return x;
            })
            .Select(x => (x.Description, Category: ToEnum<CategoryEnum>(x.Category)))
            .Where(x => x.Category.HasValue)
            .Select(x => new DescriptionClassification
            {
                Description = x.Description,
                Category = x.Category.Value
            });
    }

// Helper methods
    private string ToEnumString<T>(T value)
    {
        return JsonConvert.SerializeObject(value, new StringEnumConverter()).Replace("\"", "");
    }

    private T? ToEnum<T>(string value) where T : struct
    {
        if (value == "") return null;
        try
        {
            return JsonConvert.DeserializeObject<T>($"\"{value}\"");
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Could not convert \"{value}\" to enum {typeof(T)}");
            return null;
        }
    }
}