﻿using BS.Data;
using Newtonsoft.Json;

namespace BS.Logic.CategoryGuesser;

public class CategoryGuesserService
{
    private Dictionary<string, AccountClassification> _incomeAccounts;
    private Dictionary<string, AccountClassification> _expenseAccounts;

    public CategoryGuesserService()
    {
        ReadFiles();
    }

    public CategoryEnum? Guess(Expense expense)
    {
        CategoryGuess account = GetAccountGuess(expense);
        (CategoryEnum, double) mostLikelyCategory = account.MostLikelyCategory;
        return mostLikelyCategory.Item2 >= 0.5 ? mostLikelyCategory.Item1 : null;
    }

    private CategoryGuess GetAccountGuess(Expense expense)
    {
        var guess = new CategoryGuess();
        AccountClassification? accountClassification;
        // Income
        if (expense.Amount > 0)
            _incomeAccounts.TryGetValue(expense.Name, out accountClassification);
        else
            _expenseAccounts.TryGetValue(expense.Name, out accountClassification);
        if (accountClassification == null) return guess;

        foreach (var categoryAmount in accountClassification.CategoryAmounts)
        {
            var calc = (double)categoryAmount.Value / accountClassification.Count - 1 / (2 * (double)accountClassification.Count);
            guess.CategoryProbabilities[categoryAmount.Key] += calc;
        }

        return guess;
    }

    private void ReadFiles()
    {
        var fileNameIncome = @"C:/Users/kwlt/Desktop/incomes.json";
        var jsonStringIncome = File.ReadAllText(fileNameIncome);
        _incomeAccounts = JsonConvert.DeserializeObject<Dictionary<string, AccountClassification>>(jsonStringIncome);
        var fileNameExpense = @"C:/Users/kwlt/Desktop/expenses.json";
        var jsonStringExpense = File.ReadAllText(fileNameExpense);
        _expenseAccounts = JsonConvert.DeserializeObject<Dictionary<string, AccountClassification>>(jsonStringExpense);
    }
}