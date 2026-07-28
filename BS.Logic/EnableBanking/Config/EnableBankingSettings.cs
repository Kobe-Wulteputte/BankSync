namespace EnableBanking.Config;

/// <summary>Bound from the "EnableBanking" section of appsettings.json.</summary>
public sealed class EnableBankingSettings
{
    public string KeyPath { get; set; } = "enablebanking.key";
    public string AppKid { get; set; } = string.Empty;
    public Uri RedirectUrl { get; set; } = new("https://localhost:8080");

    /// <summary>Consent length requested when authorizing. Overridable per bank.</summary>
    public int ConsentValidityDays { get; set; } = 90;

    /// <summary>A session this close to expiry is treated as needing re-authorization.</summary>
    public int RenewBeforeDays { get; set; } = 1;

    public List<BankSettings> Banks { get; set; } = [];

    /// <summary>
    /// Normalizes every configured IBAN in place and throws on configuration that cannot work.
    /// Call once at startup so problems surface before any API call is made.
    /// </summary>
    public void NormalizeAndValidate()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bank in Banks)
        {
            if (string.IsNullOrWhiteSpace(bank.Name))
                throw new InvalidOperationException("EnableBanking:Banks contains an entry with no Name.");

            if (string.IsNullOrWhiteSpace(bank.Country))
                throw new InvalidOperationException($"EnableBanking:Banks entry '{bank.Name}' has no Country.");

            bank.Ibans = bank.Ibans.Select(NormalizeIban).Where(iban => iban.Length > 0).ToList();

            if (bank.Ibans.Count == 0)
                throw new InvalidOperationException($"EnableBanking:Banks entry '{bank.Name}' has no Ibans.");

            foreach (var iban in bank.Ibans)
            {
                if (owners.TryGetValue(iban, out var owner))
                    throw new InvalidOperationException($"IBAN {iban} is listed under both '{owner}' and '{bank.Name}'.");

                owners[iban] = bank.Name;
            }
        }

        var duplicate = Banks
            .GroupBy(bank => bank.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
            throw new InvalidOperationException(
                $"EnableBanking:Banks lists '{duplicate.Key}' more than once; put all its IBANs under a single entry.");
    }

    /// <summary>Strips spaces and punctuation and uppercases, so config formatting cannot cause a silent mismatch.</summary>
    public static string NormalizeIban(string? iban) =>
        new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

public sealed class BankSettings
{
    /// <summary>ASPSP name exactly as Enable Banking lists it, e.g. "Revolut".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Two-letter country code, e.g. "BE".</summary>
    public string Country { get; set; } = string.Empty;

    public string PsuType { get; set; } = "personal";

    /// <summary>Overrides the global value. Needed because ASPSPs cap consent length differently.</summary>
    public int? ConsentValidityDays { get; set; }

    public List<string> Ibans { get; set; } = [];
}
