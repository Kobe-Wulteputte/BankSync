using BS.Data;
using VMelnalksnis.NordigenDotNet.Accounts;

namespace BS.Logic.Workbook;

public class ExpenseService
{
    public Expense CreateExpense(BookedTransaction transaction)
    {
        return new Expense
        {
            Type = transaction.EntryReference ?? transaction.TransactionId,
            Account = transaction.DebtorAccount?.Iban ?? transaction.CreditorAccount?.Iban ?? "",
            Amount = transaction.TransactionAmount.Amount,
            Category = "",
            Date = transaction.ValueDate?.ToDateTimeUnspecified() ?? transaction.BookingDate.ToDateTimeUnspecified(),
            Description = transaction.StructuredInformation ?? transaction.UnstructuredInformation,
            Group = "",
            Name = transaction.DebtorName ?? transaction.CreditorName ?? "",
            Reimbursed = false
        };
    }
}