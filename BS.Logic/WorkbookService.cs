using BS.Data;
using ClosedXML.Excel;

namespace BS.Logic;

public class WorkbookService
{
    private readonly ExpenseService _expenseService;
    private string _activeYear = "2023";
    private IXLWorkbook? _wb;
    private IXLWorksheet? _ws;

    public WorkbookService(ExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    private void CheckIfWorkbookOpen()
    {
        if (_wb == null || _ws == null)
            throw new Exception("Workbook was not initialized before use");
    }

    public void OpenWorkBook()
    {
        _wb = new XLWorkbook("C:/Users/kwlt/Desktop/ExpensesTest.xlsx");
        _ws = _wb.Worksheet(1); //TODO: get based on year of data
    }

    public void SaveAndClose()
    {
        _wb?.Save();
        _wb = null;
        _ws = null;
    }

    public void WriteTransactions(IEnumerable<Expense> expenses)
    {
        CheckIfWorkbookOpen();
        IXLTable? table = _ws.Tables.FirstOrDefault();
        IXLRangeRow? row = table.LastRowUsed();
        foreach (Expense expense in expenses)
        {
            //TODO: validate date is in active year
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
}