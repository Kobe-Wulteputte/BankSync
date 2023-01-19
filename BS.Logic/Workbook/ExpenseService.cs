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
            Account = transaction.DebtorAccount?.Iban ?? transaction.CreditorAccount?.Iban ?? "",
            Amount = transaction.TransactionAmount.Amount,
            Category = "",
            Date = DateTime.Compare(transaction.ValueDate?.ToDateTimeUnspecified() ?? DateTime.MaxValue,
                transaction.BookingDate.ToDateTimeUnspecified()) < 0
                ? transaction.ValueDate?.ToDateTimeUnspecified()
                : transaction.BookingDate.ToDateTimeUnspecified(),
            Description = transaction.StructuredInformation ?? transaction.UnstructuredInformation,
            Group = "",
            Name = transaction.DebtorName ?? transaction.CreditorName ?? "",
            Reimbursed = false,
            Id = transaction.EntryReference ?? transaction.TransactionId
        };
    }
}