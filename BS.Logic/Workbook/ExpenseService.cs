using System.Globalization;
using BS.Data;
using EnableBanking.Models.Sessions;
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
        date ??= DateTime.UtcNow;

        return new Expense
        {
            Type = accountName,
            Account = transaction.CreditorAccount?.Iban ?? transaction.DebtorAccount?.Iban ?? "",
            Amount = transaction.TransactionAmount.Amount,
            Category = "",
            Date = date.Value,
            Description = transaction.StructuredInformation ?? transaction.UnstructuredInformation,
            Group = "",
            Name = transaction.CreditorName ?? transaction.DebtorName ?? "",
            Reimbursed = false,
            Id = transaction.EntryReference ?? transaction.TransactionId
        };
    }

    public Expense CreateExpense(EnableBanking.Models.Accounts.Transaction transaction, GetSessionResponse session)
    {
        decimal.TryParse(transaction.TransactionAmount?.Amount, CultureInfo.InvariantCulture, out decimal amount);
        DateTime.TryParse(transaction.ValueDate, out DateTime valueDate);
        if (valueDate == DateTime.MinValue) valueDate = DateTime.UtcNow;
        DateTime.TryParse(transaction.BookingDate, out DateTime bookingDate);
        if (bookingDate == DateTime.MinValue) bookingDate = DateTime.UtcNow;
        var date = DateTime.Compare(valueDate, bookingDate) < 0 ? valueDate : bookingDate;
        
        var direction = transaction.CreditDebitIndicator == "DBIT" ? -1 : 1;

        var description = transaction.RemittanceInformation?.Aggregate("", (current, info) => current + info + " ") ?? "";
        var name = transaction.Debtor?.Name ?? transaction.Creditor?.Name ?? "";
        if (string.IsNullOrEmpty(name))
        {
            name = description;
            description = "";
        }

        return new Expense()
        {
            Id = transaction.EntryReference ?? transaction.TransactionId ?? date.Ticks.ToString(),
            Type = session.Aspsp?.Name ?? "EnableBanking",
            Name = name,
            Amount = amount * direction,
            Account = transaction.DebtorAccount?.Iban ?? transaction.CreditorAccount?.Iban ?? "",
            Description = description + "(" + transaction.BankTransactionCode?.Code + ")",
            Date = date,
            Reimbursed = false,
            Category = "",
            Group = ""
        };
    }
}