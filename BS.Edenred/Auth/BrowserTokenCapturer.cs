using System.Text.RegularExpressions;
using BS.Edenred.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace BS.Edenred.Auth;

/// <summary>
/// Drives a real browser through the myEdenred login (hosted SSO at sso.eu.edenred.io,
/// incl. itsme) and captures the bearer token, API-gateway headers and account references
/// from the web app's own calls to api.be.edenred.io. The user types their own credentials;
/// this class never handles them.
/// </summary>
public sealed class BrowserTokenCapturer(EdenredSettings settings, ILogger<BrowserTokenCapturer> logger)
{
    private static readonly string[] ReusableHeaders =
    {
        "authorization", "x-client-id", "x-client-secret", "accept-language"
    };

    private static readonly string[] LoggedInMarkers =
    {
        "/xp-user/v1.0/me", "/operations", "/accounts/", "/cards"
    };

    private static readonly string[] TransactionMarkers = { "/operations" };

    private static readonly Regex OperationsRefRegex =
        new(@"/accounts/([^/?]+)/operations", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<CapturedSession> CaptureAsync(CancellationToken ct = default)
    {
        var session = new CapturedSession();
        var loginReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transactionsSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "nl-BE",
            ViewportSize = ViewportSize.NoViewport
        });
        await context.AddInitScriptAsync(
            "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        var page = await context.NewPageAsync();
        page.Console += (_, msg) => { if (msg.Type == "error") logger.LogDebug("[browser console] {Text}", msg.Text); };
        page.PageError += (_, err) => logger.LogDebug("[browser pageerror] {Error}", err);

        context.Response += async (_, response) =>
        {
            try
            {
                var url = response.Url;
                if (!IsEdenredApi(url)) return;

                var request = response.Request;
                if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase)) return;

                var requestHeaders = await request.AllHeadersAsync();
                lock (gate)
                {
                    foreach (var name in ReusableHeaders)
                        if (requestHeaders.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                            session.AuthHeaders[NormalizeHeaderName(name)] = value;

                    foreach (Match m in OperationsRefRegex.Matches(url))
                        session.AccountRefs.Add(m.Groups[1].Value);
                }

                var responseHeaders = await response.AllHeadersAsync();
                var contentType = responseHeaders.TryGetValue("content-type", out var ct2) ? ct2 : null;
                if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    string? body = null;
                    try { body = await response.TextAsync(); } catch { /* body unavailable */ }
                    lock (gate)
                    {
                        session.Responses.Add(new CapturedResponse
                        {
                            Url = url, Status = response.Status, Method = request.Method,
                            ContentType = contentType, Body = body
                        });
                    }
                }

                if (response.Status is >= 200 and < 300)
                {
                    if (LoggedInMarkers.Any(m => url.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        loginReady.TrySetResult(true);
                    if (TransactionMarkers.Any(m => url.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        transactionsSeen.TrySetResult(true);
                }
            }
            catch
            {
                // A listener must never crash the capture.
            }
        };

        logger.LogInformation("Opening browser at {LoginUrl}", settings.LoginUrl);
        logger.LogInformation("Log in with your myEdenred credentials (email, password, any MFA / itsme), then open your transactions page.");

        await page.GotoAsync(settings.LoginUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        logger.LogInformation("Navigated to: {Url}", page.Url);

        using (var loginCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            loginCts.CancelAfter(TimeSpan.FromSeconds(settings.LoginTimeoutSeconds));
            try { await loginReady.Task.WaitAsync(loginCts.Token); }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"No authenticated Edenred API call was observed within {settings.LoginTimeoutSeconds}s. Login was not completed.");
            }
        }

        logger.LogInformation("Login detected. If your transactions aren't shown yet, open your history page now...");

        using (var captureCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            captureCts.CancelAfter(TimeSpan.FromSeconds(settings.PostLoginCaptureSeconds));
            try { await transactionsSeen.Task.WaitAsync(captureCts.Token); }
            catch (OperationCanceledException) { /* best effort */ }
        }

        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 8_000 }); }
        catch { /* SPAs may never reach full idle */ }
        await Task.Delay(TimeSpan.FromSeconds(settings.PostLoginSettleSeconds), ct);

        try { session.CookieCount = (await context.CookiesAsync()).Count; }
        catch { /* cookies optional */ }

        return session;
    }

    private static bool IsEdenredApi(string url)
    {
        if (!url.Contains("edenred", StringComparison.OrdinalIgnoreCase)) return false;
        return url.Contains("api.be.edenred.io", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/api/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/connect/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var args = new[] { "--disable-blink-features=AutomationControlled" };
        var ignore = new[] { "--enable-automation" };

        // Prefer the user's real Chrome (least likely to trip bot detection);
        // fall back to Playwright's bundled Chromium if Chrome isn't installed.
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false, Channel = "chrome", Args = args, IgnoreDefaultArgs = ignore
            });
            logger.LogInformation("Using installed Google Chrome.");
            return browser;
        }
        catch
        {
            logger.LogInformation("Google Chrome not available; using Playwright's bundled Chromium.");
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false, Args = args, IgnoreDefaultArgs = ignore
            });
        }
    }

    private static string NormalizeHeaderName(string lower) => lower switch
    {
        "authorization" => "Authorization",
        "x-client-id" => "X-Client-Id",
        "x-client-secret" => "X-Client-Secret",
        "accept-language" => "Accept-Language",
        _ => lower
    };
}
