using Mone.ScreenshotHarness;

// Resolve config from args (--key value) then env vars then defaults.
// Defaults target the docker compose stack: the API serves the dashboard at http://localhost:8080.
var args0 = ParseArgs(args);

string Get(string argKey, string envKey, string fallback) =>
    args0.TryGetValue(argKey, out var a) && !string.IsNullOrWhiteSpace(a) ? a
    : Environment.GetEnvironmentVariable(envKey) is { Length: > 0 } e ? e
    : fallback;

var config = new CaptureConfig(
    BaseUrl: Get("base-url", "SCREENSHOT_BASE_URL", "http://localhost:8080").TrimEnd('/'),
    OutputDir: Get("output-dir", "SCREENSHOT_OUTPUT_DIR", Path.Combine("Mone", "artifacts", "screenshots")),
    Email: Get("email", "SCREENSHOT_EMAIL", "admin@mone.local"),
    Password: Get("password", "SCREENSHOT_PASSWORD", "Admin123!"));

Console.WriteLine($"harness base-url={config.BaseUrl} output-dir={config.OutputDir} email={config.Email} screens={ScreenManifest.All.Count}");

await using var session = new CaptureSession(config);
return await session.RunAsync();

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
            continue;
        key = key[2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            result[key] = args[++i];
        else
            result[key] = "true";
    }
    return result;
}
