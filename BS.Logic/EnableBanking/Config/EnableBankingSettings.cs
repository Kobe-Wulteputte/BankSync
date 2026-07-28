namespace EnableBanking.Config;

/// <summary>Bound from the "EnableBanking" section of appsettings.json.</summary>
public sealed class EnableBankingSettings
{
    public string KeyPath { get; set; } = "enablebanking.key";
    public string AppKid { get; set; } = string.Empty;
    public Uri RedirectUrl { get; set; } = new("https://localhost:8080");

    /// <summary>
    /// Sent as the <c>psu-ip-address</c> header, which some ASPSPs list in their
    /// <c>required_psu_headers</c> metadata (Argenta does; Revolut does not). Leave unset to fall
    /// back to the machine's local IPv4, and set it explicitly if an ASPSP rejects a private address.
    /// </summary>
    public string PsuIpAddress { get; set; } = string.Empty;

    /// <summary>Consent length requested when authorizing. Overridable per bank.</summary>
    public int ConsentValidityDays { get; set; } = 90;

    /// <summary>A session this close to expiry is treated as needing re-authorization.</summary>
    public int RenewBeforeDays { get; set; } = 1;

    /// <summary>
    /// How long an emailed authorization link is considered outstanding. While one is outstanding
    /// the console will not email that bank again; past this age it is discarded and a fresh link
    /// generated, since Enable Banking authorization links do not stay valid indefinitely.
    /// </summary>
    public int AuthorizationLinkTtlHours { get; set; } = 24;

    public List<BankSettings> Banks { get; set; } = [];

    /// <summary>
    /// Normalizes every configured IBAN in place and throws on configuration that cannot work.
    /// Call once at startup so problems surface before any API call is made.
    /// </summary>
    public void NormalizeAndValidate()
    {
        if (string.IsNullOrWhiteSpace(AppKid))
            throw new InvalidOperationException("EnableBanking:AppKid is not configured.");

        if (string.IsNullOrWhiteSpace(KeyPath))
            throw new InvalidOperationException("EnableBanking:KeyPath is not configured.");

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < Banks.Count; index++)
        {
            var bank = Banks[index];

            bank.Name = bank.Name.Trim();
            bank.Country = bank.Country.Trim();

            if (string.IsNullOrWhiteSpace(bank.Name))
                throw new InvalidOperationException($"EnableBanking:Banks[{index}] has no Name.");

            if (string.IsNullOrWhiteSpace(bank.Country))
                throw new InvalidOperationException($"EnableBanking:Banks entry '{bank.Name}' has no Country.");

            bank.Ibans = bank.Ibans.Select(raw =>
            {
                var normalized = NormalizeIban(raw);
                if (normalized.Length == 0)
                    throw new InvalidOperationException(
                        $"EnableBanking:Banks entry '{bank.Name}' has an invalid IBAN: '{raw}'.");

                return normalized;
            }).ToList();

            // An empty list is legitimate: it means sync every account the session exposes. That is
            // the only workable mode for ASPSPs whose accounts have no IBAN, PayPal being the case
            // this was added for. Both Connect and sync log when a bank is in this mode, so a list
            // omitted by accident does not silently widen what gets synced.

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
        new string((iban ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
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

    /// <summary>
    /// Set for ASPSPs that will not honour a pre-specified account list. When true the
    /// authorization request omits <c>access.accounts</c> entirely and the user picks accounts in
    /// the bank's own screens; <see cref="Ibans"/> then acts purely as a filter on what gets synced.
    /// Argenta needs this — it silently dropped a requested IBAN list and granted no accounts.
    /// Revolut honours the list, so it can stay false.
    /// </summary>
    public bool SelectAccountsAtBank { get; set; }

    public List<string> Ibans { get; set; } = [];

    /// <summary>
    /// True when no IBANs are listed, meaning every account the session exposes is synced.
    /// Required for ASPSPs whose accounts have no IBAN, such as PayPal.
    /// </summary>
    public bool SyncsAllAccounts => Ibans.Count == 0;
}
