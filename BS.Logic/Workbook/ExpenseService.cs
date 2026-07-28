using System.Globalization;
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

    public Expense CreateExpense(EdenredOperation operation)
    {
        var product = operation.TransactionDetails?.ProductRef switch
        {
            "TRE" => "Meal voucher",
            "ECE" => "Eco voucher",
            _ => operation.TransactionDetails?.ProductRef ?? ""
        };

        var descriptionParts = new[] { product, operation.Type, operation.Reason }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var description = string.Join(" · ", descriptionParts);

        var amountInCents = operation.TransactionDetails?.Amount ?? 0;

        return new Expense
        {
            Type = "Edenred",
            Amount = amountInCents / 100m, // sign preserved: debit negative, credit positive
            Date = operation.Date,
            Account = "",
            Name = operation.Outlet?.OutletName ?? "",
            Category = "",
            Group = "",
            Reimbursed = false,
            Description = description,
            Id = "EDENRED-" + operation.OperationRef
        };
    }

    public Expense CreateExpense(EnableBanking.Models.Accounts.Transaction transaction, string bankName)
    {
        decimal.TryParse(transaction.TransactionAmount?.Amount, CultureInfo.InvariantCulture, out decimal amount);
        DateTime.TryParse(transaction.ValueDate, out DateTime valueDate);
        if (valueDate == DateTime.MinValue) valueDate = DateTime.UtcNow;
        DateTime.TryParse(transaction.BookingDate, out DateTime bookingDate);
        if (bookingDate == DateTime.MinValue) bookingDate = DateTime.UtcNow;
        var date = DateTime.Compare(valueDate, bookingDate) < 0 ? valueDate : bookingDate;

        var direction = transaction.CreditDebitIndicator == "DBIT" ? -1 : 1;

        var description = transaction.RemittanceInformation?.Aggregate("", (current, info) => current + info + " ") ?? "";
        description = description.Trim();
        var name = direction == -1 ? transaction.Creditor?.Name : transaction.Debtor?.Name;
        if (string.IsNullOrEmpty(name))
        {
            name = description;
        }

        if (name == description)
        {
            description = "";
        }

        return new Expense()
        {
            Id = transaction.EntryReference ?? transaction.TransactionId ?? date.Ticks.ToString(),
            Type = bankName,
            Name = name,
            Amount = amount * direction,
            Account = transaction.DebtorAccount?.Iban ?? transaction.CreditorAccount?.Iban ?? "",
            Description = description + "(" + transaction.BankTransactionCode?.Code + " " + transaction.DebtorAccountAdditionalIdentification?[0]?.Identification +
                          transaction.CreditorAccountAdditionalIdentification?[0]?.Identification + ")",
            Date = date,
            Reimbursed = false,
            Category = "",
            Group = ""
        };
    }
}