using BS.Data;
using VMelnalksnis.NordigenDotNet.Accounts;

namespace BS.Logic.Workbook;

public class ExpenseService
{
    public Expense CreateExpense(BookedTransaction transaction, string accountName)
    {
        return new Expense
        {
            Type = accountName,
            Account = transaction.CreditorAccount?.Iban ?? transaction.DebtorAccount?.Iban ?? "",
            Amount = transaction.TransactionAmount.Amount,
            Category = "",
            Date = DateTime.Compare(transaction.ValueDate?.ToDateTimeUnspecified() ?? DateTime.MaxValue,
                transaction.BookingDate.ToDateTimeUnspecified()) < 0
                ? transaction.ValueDate?.ToDateTimeUnspecified()
                : transaction.BookingDate.ToDateTimeUnspecified(),
            Description = transaction.StructuredInformation ?? transaction.UnstructuredInformation,
            Group = "",
            Name = transaction.CreditorName ?? transaction.DebtorName ?? "",
            Reimbursed = false,
            Id = transaction.EntryReference ?? transaction.TransactionId
        };
    }
}