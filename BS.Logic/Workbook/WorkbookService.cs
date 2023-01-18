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
        if (_wb.Worksheets.TryGetWorksheet(name, out IXLWorksheet? ws)) return ws;
        ws = _wb.Worksheets.Add(name);
        ws.Cell(1, 1).InsertTable(new List<Expense>(), "table" + name);
        ws.Columns().AdjustToContents();

        return ws;
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
            if (expense.Category != "")
                row.Cell(6).Style.Font.Italic = true;
            row.Cell(7).SetValue(expense.Group);
            row.Cell(8).SetValue(expense.Reimbursed ? "TRUE" : "");
            row.Cell(9).SetValue(expense.Description);
            row.Cell(10).SetValue(expense.Id);
        }

        table.Resize(table.FirstCellUsed(), row.Cell(10));
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
                Description = x.Cell(9).GetValue<string>(),
                Id = x.Cell(10).GetValue<string>()
            });

        return expenses;
    }

    public IEnumerable<Expense> GetAllExpenses()
    {
        CheckIfWorkbookOpen();
        var expenses = Enumerable.Empty<Expense>();
        foreach (IXLWorksheet? worksheet in _wb.Worksheets)
        {
            if (!Regex.Match(worksheet.Name, @"\d{4}").Success) continue;
            expenses = expenses.Concat(GetAllExpensesOfWorksheet(worksheet));
        }

        return expenses;
    }

    public IEnumerable<Expense> RemoveDuplicates(IEnumerable<Expense> expenses)
    {
        var ids = new HashSet<string>();
        foreach (IXLWorksheet? worksheet in _wb.Worksheets.Where(ws => Regex.Match(ws.Name, @"\d{4}").Success))
            ids.UnionWith(worksheet
                .RangeUsed()
                .RowsUsed()
                .Skip(1)
                .Select(x => x.Cell(10).GetValue<string>())
                .ToHashSet());

        return expenses.Where(x => !ids.Contains(x.Id));
    }
}