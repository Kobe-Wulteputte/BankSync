using System.Text.Json.Serialization;

namespace BS.Data;

/// <summary>Envelope returned by GET api.be.edenred.io/xp-user/v1.0/accounts/accountRef/operations.</summary>
public class EdenredOperationsResponse
{
    [JsonPropertyName("data")] public List<EdenredOperation> Data { get; set; } = new();
}

/// <summary>A single myEdenred card operation (transaction).</summary>
public class EdenredOperation
{
    [JsonPropertyName("operation_ref")] public string OperationRef { get; set; } = "";

    /// <summary>"balance_debit" or "balance_credit".</summary>
    [JsonPropertyName("movement")] public string? Movement { get; set; }

    /// <summary>"redemption" (spend) or "top-up".</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("currency")] public string? Currency { get; set; }

    [JsonPropertyName("date")] public DateTime Date { get; set; }

    /// <summary>Free-text reason, present on top-ups ("22 * 9,15Euro") and some redemptions.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; set; }

    [JsonPropertyName("outlet")] public EdenredOutlet? Outlet { get; set; }

    [JsonPropertyName("transaction_details")] public EdenredTransactionDetails? TransactionDetails { get; set; }
}

public class EdenredOutlet
{
    [JsonPropertyName("outlet_name")] public string? OutletName { get; set; }
    [JsonPropertyName("outlet_ref")] public string? OutletRef { get; set; }
}

public class EdenredTransactionDetails
{
    /// <summary>"TRE" = meal vouchers, "ECE" = eco vouchers.</summary>
    [JsonPropertyName("product_ref")] public string? ProductRef { get; set; }

    /// <summary>Amount in cents, signed (debit negative, credit positive).</summary>
    [JsonPropertyName("amount")] public long Amount { get; set; }
}
