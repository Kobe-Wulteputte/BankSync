using BS.Data;
using VMelnalksnis.NordigenDotNet.Accounts;

namespace BS.Logic.Workbook;

public class ExpenseService
{
    public Expense CreateExpense(BookedTransaction transaction, string accountName)
    {
        var date = DateTime.Compare(transaction.ValueDate?.ToDateTimeUnspecified() ?? DateTime.MaxValue,
            transaction.BookingDate?.ToDateTimeUnspecified() ?? DateTime.MaxValue) < 0
            ? transaction.ValueDate?.ToDateTimeUnspecified()
            : transaction.BookingDate?.ToDateTimeUnspecified();
        return new Expense
        {
            Type = accountName,
            Account = transaction.CreditorAccount?.Iban ?? transaction.DebtorAccount?.Iban ?? "",
            Amount = transaction.TransactionAmount.Amount,
            Category = "",
            Date = date,
            Description = transaction.StructuredInformation ?? transaction.UnstructuredInformation,
            Group = "",
            Name = transaction.CreditorName ?? transaction.DebtorName ?? "",
            Reimbursed = false,
            Id = transaction.EntryReference ?? transaction.TransactionId
        };
    }
}