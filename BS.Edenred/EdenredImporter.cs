using System.Text.Json;
using BS.Data;
using BS.Edenred.Api;
using BS.Edenred.Auth;
using BS.Logic.CategoryGuesser;
using BS.Logic.Workbook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BS.Edenred;

/// <summary>
/// Orchestrates a myEdenred import: browser login -> fetch operations -> map to Expense ->
/// dedupe -> AI category guess -> append to the shared Expenses workbook. Mirrors the
/// bank-transaction flow in BS.Logic.Application.
/// </summary>
public sealed class EdenredImporter(
    ILogger<EdenredImporter> logger,
    IConfiguration configuration,
    BrowserTokenCapturer capturer,
    EdenredApiClient apiClient,
    ExpenseService expenseService,
    WorkbookService workbookService,
    AiCategoryGuesserService categoryGuesser)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task Run(CancellationToken ct = default)
    {
        logger.LogInformation("Starting myEdenred import");

        // 1) Log in through the browser and capture the auth headers + account references.
        var session = await capturer.CaptureAsync(ct);
        if (!session.HasBearerToken)
        {
            logger.LogError("No bearer token captured from the browser session; aborting.");
            return;
        }

        // 2) Fetch operations for each account (accountRef is a literal path segment on this API).
        apiClient.UseHeaders(session.AuthHeaders);
        var accountRefs = session.AccountRefs.Count > 0 ? session.AccountRefs.ToList() : new List<string> { "accountRef" };

        var operations = new List<EdenredOperation>();
        foreach (var accountRef in accountRefs)
        {
            var result = await apiClient.GetOperationsAsync(accountRef, ct);
            if (result.Status != 200 || string.IsNullOrWhiteSpace(result.Body))
            {
                logger.LogWarning("operations[{AccountRef}] returned HTTP {Status}", accountRef, result.Status);
                continue;
            }

            var parsed = System.Text.Json.JsonSerializer.Deserialize<EdenredOperationsResponse>(result.Body, JsonOptions);
            if (parsed?.Data is { Count: > 0 })
                operations.AddRange(parsed.Data);
        }

        logger.LogInformation("Fetched {Count} Edenred operations", operations.Count);
        if (operations.Count == 0)
        {
            logger.LogWarning("No operations found; nothing to write.");
            return;
        }

        // 3) Map to expenses and drop any duplicate operation refs within this batch.
        var expenses = operations
            .Select(op => expenseService.CreateExpense(op))
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .ToList();

        // 4) Open the shared workbook and remove rows already present.
        var filePath = configuration["FilePaths:Expenses"];
        if (string.IsNullOrWhiteSpace(filePath) || !workbookService.OpenWorkBook(filePath))
        {
            logger.LogError("Could not open workbook at '{FilePath}'", filePath);
            return;
        }

        expenses = workbookService.RemoveDuplicates(expenses).ToList();
        logger.LogInformation("{Count} new Edenred transactions to add", expenses.Count);

        // 5) Guess a category for each new transaction (same guesser as the bank flow).
        foreach (var expense in expenses)
        {
            var category = await categoryGuesser.Guess(expense);
            expense.Category = category.HasValue
                ? JsonConvert.SerializeObject(category, new StringEnumConverter()).Replace("\"", "")
                : "";
        }

        // 6) Write grouped by year, then save.
        foreach (var grouping in expenses.OrderBy(x => x.Date).GroupBy(x => x.Date.Year))
            workbookService.WriteTransactions(grouping, workbookService.GetWorksheet(grouping.Key.ToString()));

        workbookService.SaveAndClose();
        logger.LogInformation("Done. Added {Count} Edenred transactions to {FilePath}", expenses.Count, filePath);
    }
}
