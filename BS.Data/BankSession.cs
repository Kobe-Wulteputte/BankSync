using System.Text.Json.Serialization;

namespace BS.Data;

/// <summary>One Enable Banking session, covering every account authorized at a single bank.</summary>
public sealed class BankSession
{
    public Guid SessionId { get; set; }

    /// <summary>ASPSP name, matching EnableBanking:Banks[].Name.</summary>
    public string Bank { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public List<StoredAccount> Accounts { get; set; } = [];

    public DateTime? ValidUntil { get; set; }

    /// <summary>True when this record still needs its metadata resolved from the API.</summary>
    [JsonIgnore]
    public bool IsIncomplete => string.IsNullOrWhiteSpace(Bank) || Accounts.Count == 0;
}

public sealed class StoredAccount
{
    /// <summary>Enable Banking account UID, used directly as GetTransactionsRequest.AccountId.</summary>
    public Guid Uid { get; set; }

    public string Iban { get; set; } = string.Empty;
}
