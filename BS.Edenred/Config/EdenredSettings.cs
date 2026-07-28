namespace BS.Edenred.Config;

/// <summary>Bound from the "Edenred" section of appsettings.json.</summary>
public sealed class EdenredSettings
{
    public string LoginUrl { get; set; } = "https://myedenred.be";
    public string ApiBaseUrl { get; set; } = "https://api.be.edenred.io/xp-user/v1.0/";
    public int LoginTimeoutSeconds { get; set; } = 300;
    public int PostLoginCaptureSeconds { get; set; } = 90;
    public int PostLoginSettleSeconds { get; set; } = 4;
}
