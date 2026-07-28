namespace BS.Edenred.Auth;

/// <summary>A single JSON API response observed in the browser session.</summary>
public sealed class CapturedResponse
{
    public required string Url { get; init; }
    public required int Status { get; init; }
    public required string Method { get; init; }
    public string? ContentType { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Everything harvested from the authenticated myEdenred browser session: the reusable
/// auth headers, the raw JSON API responses observed, and the account references discovered.
/// </summary>
public sealed class CapturedSession
{
    public Dictionary<string, string> AuthHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CapturedResponse> Responses { get; } = new();
    public HashSet<string> AccountRefs { get; } = new(StringComparer.Ordinal);
    public int CookieCount { get; set; }

    public bool HasBearerToken => AuthHeaders.ContainsKey("Authorization");
    public bool HasData => Responses.Count > 0;
}
