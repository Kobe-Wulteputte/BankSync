namespace BS.Data;

/// <summary>
/// An authorization link that has been generated and emailed but not yet completed.
/// Persisting it does two jobs: the callback endpoint can verify the returned state instead of
/// trusting any code posted at it, and the console can avoid re-sending the same link every run.
/// </summary>
public sealed class PendingAuthorization
{
    /// <summary>Opaque value round-tripped through the bank, matched when the callback arrives.</summary>
    public string State { get; set; } = string.Empty;

    public string Bank { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
