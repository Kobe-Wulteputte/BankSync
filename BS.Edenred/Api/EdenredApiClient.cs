using BS.Edenred.Config;

namespace BS.Edenred.Api;

public sealed class FetchResult
{
    public required string Url { get; init; }
    public required int Status { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Calls the Edenred XP API by replaying the header set captured from the authenticated
/// browser session, with a fresh X-Correlation-Id per request.
/// </summary>
public sealed class EdenredApiClient(EdenredSettings settings) : IDisposable
{
    private readonly HttpClient _http = new();
    private IReadOnlyDictionary<string, string> _authHeaders = new Dictionary<string, string>();

    public void UseHeaders(IReadOnlyDictionary<string, string> authHeaders) => _authHeaders = authHeaders;

    public async Task<FetchResult> GetRawAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in _authHeaders)
            request.Headers.TryAddWithoutValidation(name, value);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return new FetchResult { Url = url, Status = (int)response.StatusCode, Body = body };
    }

    public Task<FetchResult> GetOperationsAsync(string accountRef, CancellationToken ct = default)
    {
        var baseUrl = settings.ApiBaseUrl.TrimEnd('/');
        return GetRawAsync($"{baseUrl}/accounts/{accountRef}/operations", ct);
    }

    public void Dispose() => _http.Dispose();
}
