using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Mone.ScreenshotHarness;

/// <summary>
/// Resolved runtime configuration for a capture run. Built from args/env in Program.cs.
/// </summary>
public sealed record CaptureConfig(string BaseUrl, string OutputDir, string Email, string Password);

/// <summary>JSON shape returned by POST /api/auth/login (mirrors Mone.Api.Models.TokenResponse).</summary>
internal sealed record TokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires")] DateTime Expires);

/// <summary>JSON shape returned by the host endpoints (subset of Mone.Api.Models.HostResponse).</summary>
internal sealed record HostResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>
/// Drives the live WASM dashboard with headless Chromium and saves a full-page PNG for every
/// <see cref="ScreenManifest"/> screen in both Light and Dark themes. The session authenticates
/// against the API over HTTP, seeds a host so host-scoped routes resolve, and injects the JWT into
/// localStorage (matching <c>AuthService</c> keys) so the SPA boots authenticated.
/// </summary>
public sealed class CaptureSession : IAsyncDisposable
{
    private const string TokenKey = "authToken";
    private const string ExpiresKey = "authTokenExpires";
    private static readonly string[] Themes = { "Light", "Dark" };

    private readonly CaptureConfig _config;
    private readonly HttpClient _http;

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public CaptureSession(CaptureConfig config)
    {
        _config = config;
        _http = new HttpClient { BaseAddress = new Uri(config.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Runs the full capture. Returns 0 when every required screen×theme rendered, non-zero otherwise.
    /// The Login screen is captured best-effort and never fails the run.
    /// </summary>
    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_config.OutputDir);

        TokenResponse token;
        Guid hostId;
        try
        {
            token = await LoginAsync();
            hostId = await SeedHostAsync(token.Token);
        }
        catch (Exception ex)
        {
            Log("FATAL", "-", "-", "setup", $"FAIL: {ex.Message}");
            return 1;
        }

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        var failures = new List<string>();
        var captured = 0;
        var attempted = 0;

        foreach (var theme in Themes)
        {
            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                BaseURL = _config.BaseUrl
            });

            // Inject the JWT before any page script runs so every navigation boots authenticated.
            await context.AddInitScriptAsync(
                $"localStorage.setItem('{TokenKey}', {JsonSerializer.Serialize(token.Token)});" +
                $"localStorage.setItem('{ExpiresKey}', {JsonSerializer.Serialize(token.Expires.ToString("O", CultureInfo.InvariantCulture))});");

            var page = await context.NewPageAsync();

            // Land on an authenticated screen and switch the theme via the MainLayout menu.
            await NavigateAuthenticatedAsync(page, "/");
            await ApplyThemeAsync(page, theme);

            foreach (var screen in ScreenManifest.All)
            {
                var route = screen.Route.Replace("{id}", hostId.ToString());
                var output = Path.Combine(_config.OutputDir, $"{screen.Name}-{theme}.png");
                attempted++;

                try
                {
                    if (screen.RequiresAuth)
                    {
                        await NavigateAuthenticatedAsync(page, route);
                    }
                    else
                    {
                        // Login is anonymous: drop the token so the login form renders. Best-effort.
                        await CaptureAnonymousAsync(context, route, output, screen.Name, theme);
                        Log(screen.Name, theme, route, output, "OK (anonymous)");
                        captured++;
                        continue;
                    }

                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = output, FullPage = true });
                    Log(screen.Name, theme, route, output, "OK");
                    captured++;
                }
                catch (Exception ex)
                {
                    if (!screen.RequiresAuth)
                    {
                        // Login is best-effort; record but do not count as a required failure.
                        Log(screen.Name, theme, route, output, $"SKIP (best-effort): {ex.Message}");
                        continue;
                    }

                    failures.Add($"{screen.Name}-{theme}");
                    Log(screen.Name, theme, route, output, $"FAIL: {ex.Message}");
                }
            }
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"SUMMARY: FAIL — {captured}/{attempted} captured; missing: {string.Join(", ", failures)}");
            return 1;
        }

        Console.WriteLine($"SUMMARY: OK — {captured}/{attempted} captured to {_config.OutputDir}");
        return 0;
    }

    private async Task<TokenResponse> LoginAsync()
    {
        using var response = await _http.PostAsJsonAsync("/api/auth/login", new { Email = _config.Email, Password = _config.Password });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"login returned {(int)response.StatusCode}: {body}");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (token is null || string.IsNullOrEmpty(token.Token))
            throw new InvalidOperationException("login succeeded but returned no token");

        return token;
    }

    private async Task<Guid> SeedHostAsync(string bearer)
    {
        var name = $"screenshot-host-{DateTime.UtcNow:yyyyMMddHHmmss}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/hosts/")
        {
            Content = JsonContent.Create(new { Name = name, Address = "10.10.0.1", Enabled = true })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return await RecoverHostIdAsync(bearer);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"host seed returned {(int)response.StatusCode}: {body}");
        }

        var host = await response.Content.ReadFromJsonAsync<HostResponse>();
        if (host is null || host.Id == Guid.Empty)
            throw new InvalidOperationException("host seed succeeded but returned no id");

        return host.Id;
    }

    private async Task<Guid> RecoverHostIdAsync(string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/hosts/");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var hosts = await response.Content.ReadFromJsonAsync<List<HostResponse>>();
        if (hosts is null || hosts.Count == 0)
            throw new InvalidOperationException("host seed conflicted but no existing host could be recovered");

        return hosts[0].Id;
    }

    private static async Task NavigateAuthenticatedAsync(IPage page, string route)
    {
        await page.GotoAsync(route, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
        // The authenticated MudLayout renders the app bar; waiting on it confirms the SPA booted authenticated.
        await page.WaitForSelectorAsync(".mud-appbar", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForSelectorAsync(".mud-main-content", new PageWaitForSelectorOptions { Timeout = 30000 });
    }

    private async Task CaptureAnonymousAsync(IBrowserContext context, string route, string output, string name, string theme)
    {
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(route, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
            // Clear the injected token, then reload so the login form (NotAuthorized branch) renders.
            await page.EvaluateAsync($"localStorage.removeItem('{TokenKey}'); localStorage.removeItem('{ExpiresKey}');");
            await page.GotoAsync(route, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
            await page.WaitForSelectorAsync(".mud-layout", new PageWaitForSelectorOptions { Timeout = 30000 });
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = output, FullPage = true });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task ApplyThemeAsync(IPage page, string theme)
    {
        // Open the MainLayout 'Theme mode' menu and click the matching item.
        var menu = page.Locator("[aria-label='Theme mode']");
        await menu.ClickAsync(new LocatorClickOptions { Timeout = 15000 });

        // MudBlazor renders MudMenuItem as <div class="mud-menu-item ...">Light</div> with no
        // role="menuitem", and the item also wraps an icon span, so :text-is matches nothing.
        // Filter the item class by exact text instead of GetByRole.
        var item = page.Locator(".mud-menu-item").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^{theme}$") });
        await item.ClickAsync(new LocatorClickOptions { Timeout = 15000 });

        // Wait for the theme to apply by polling the body's effective background luminance.
        var wantDark = theme == "Dark";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var isDark = await page.EvaluateAsync<bool>(
                "() => { const c = getComputedStyle(document.body).backgroundColor; " +
                "const m = c.match(/\\d+/g); if (!m) return false; " +
                "const [r,g,b] = m.map(Number); return (0.299*r + 0.587*g + 0.114*b) < 128; }");
            if (isDark == wantDark)
                return;
            await page.WaitForTimeoutAsync(150);
        }
    }

    private static void Log(string screen, string theme, string route, string path, string result) =>
        Console.WriteLine($"capture screen={screen} theme={theme} route={route} path={path} result={result}");

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
        _http.Dispose();
    }
}
