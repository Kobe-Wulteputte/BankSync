using System.Text.RegularExpressions;
using BS.Data;
using ClosedXML.Excel;

namespace BS.Logic.Workbook;

public class WorkbookService
{
    private readonly ExpenseService _expenseService;
    private IXLWorkbook? _wb;

    public WorkbookService(ExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    private void CheckIfWorkbookOpen()
    {
        if (_wb == null)
            throw new Exception("Workbook was not initialized before use");
    }

    /// <summary>
    /// Will open the workbook and set the active worksheet to the first sheet
    /// </summary>
    /// <param name="filePath">Path of the excel file</param>
    public void OpenWorkBook(string filePath)
    {
        _wb = new XLWorkbook(filePath);
    }

    public void SaveAndClose()
    {
        _wb?.Save();
        _wb = null;
    }

    public IXLWorksheet GetWorksheet(string name)
    {
        CheckIfWorkbookOpen();
        return _wb.Worksheet(name);
    }

    public void WriteTransactions(IEnumerable<Expense> expenses, IXLWorksheet ws)
    {
        CheckIfWorkbookOpen();
        IXLTable? table = ws.Tables.FirstOrDefault();
        IXLRangeRow? row = table.LastRowUsed();
        foreach (Expense expense in expenses)
        {
            row = row.RowBelow();
            row.Cell(1).SetValue(expense.Type);
            row.Cell(2).SetValue(expense.Amount);
            row.Cell(2).Style.NumberFormat.Format = "_ * # ##0.00_ ;_ * -# ##0.00_ ;_ * \"-\"??_ ;_ @_ ";
            row.Cell(3).SetValue(expense.Date);
            row.Cell(4).SetValue(expense.Account);
            row.Cell(5).SetValue(expense.Name);
            row.Cell(6).SetValue(expense.Category);
            row.Cell(7).SetValue(expense.Group);
            row.Cell(8).SetValue(expense.Reimbursed ? "TRUE" : "");
            row.Cell(9).SetValue(expense.Description);
        }

        table.Resize(table.FirstCellUsed(), row.Cell(9));
    }

    public IEnumerable<Expense> GetAllExpensesOfWorksheet(IXLWorksheet ws)
    {
        var expenses = ws
            .RangeUsed()
            .RowsUsed()
            .Skip(1)
            .Select(x => new Expense
            {
                Type = x.Cell(1).GetValue<string>(),
                Amount = x.Cell(2).GetValue<decimal>(),
                Date = x.Cell(3).GetValue<DateTime>(),
                Account = x.Cell(4).GetValue<string>(),
                Name = x.Cell(5).GetValue<string>(),
                Category = x.Cell(6).GetValue<string>(),
                Group = x.Cell(7).GetValue<string>(),
                Reimbursed = x.Cell(8).GetValue<string>() == "TRUE",
                Description = x.Cell(9).GetValue<string>()
            });

        return expenses;
    }

    public IEnumerable<Expense> GetAllExpenses()
    {
        CheckIfWorkbookOpen();
        var expenses = Enumerable.Empty<Expense>();
        foreach (var worksheet in _wb.Worksheets)
        {
            if (!Regex.Match(worksheet.Name, @"\d{4}").Success) continue;
            expenses = expenses.Concat(GetAllExpensesOfWorksheet(worksheet));
        }

        return expenses;
    }
}