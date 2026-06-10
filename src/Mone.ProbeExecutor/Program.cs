using System.Security.Cryptography;
using System.Text;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Messaging.Extensions;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Data;
using Mone.ProbeExecutor.Jobs;
using Mone.ProbeExecutor.Services;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Remote executors keep no database: config is pulled from the console API and cached locally, and
// results go out over NATS (spooled to local SQLite when NATS is unreachable). createStreams:false
// means startup does not depend on NATS being up.
builder.Services.AddSingleton<SpoolStore>();

builder.Services.AddMoneMessaging(
    builder.Configuration.GetConnectionString("Nats") ?? "nats://localhost:4222",
    createStreams: false);

builder.Services.AddSingleton(sp =>
    new Mone.PluginEngine.PluginEngine(
        sp.GetRequiredService<ILogger<Mone.PluginEngine.PluginEngine>>(), enableHotReload: false));

builder.Services.AddQuartz();

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp =>
    Mone.Infrastructure.Services.NodeIdentity.Resolve(
        sp.GetRequiredService<IConfiguration>(), Mone.Contracts.Models.ExecutorRole.Probe));
builder.Services.AddHostedService<Mone.Infrastructure.Services.NodeRegistrationService>();

builder.Services.AddSingleton<IProbeConfigSource, ApiProbeConfigSource>();
builder.Services.AddSingleton<IResultSink, SpoolingResultSink>();
builder.Services.AddHostedService<SpoolForwarderService>();

builder.Services.AddTransient<ProbeExecutionJob>();
builder.Services.AddSingleton<ProbeSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProbeSchedulerService>());
builder.Services.AddHostedService<ProbeScheduleListenerService>();
builder.Services.AddHostedService<ProbeTriggerListenerService>();
builder.Services.AddHostedService<UdpListenerService>();
builder.Services.AddHostedService<Mone.PluginEngine.PluginReloadListener>();

var app = builder.Build();

app.MapPost("/api/webhooks/{targetId}", async (
    string targetId,
    HttpContext httpContext,
    Mone.PluginEngine.PluginEngine pluginEngine,
    IProbeConfigSource configSource,
    IResultSink resultSink,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Webhook received for target {TargetId}, ContentLength={ContentLength}",
        targetId, httpContext.Request.ContentLength);

    var passivePlugins = pluginEngine.Registry
        .GetAll()
        .Where(r => r.Metadata.Kind == Mone.PluginEngine.PluginKind.Probe
                  && r.Metadata.ProbeMode == ProbeMode.Passive
                  && r.Plugin is IPassiveProbePlugin)
        .ToList();

    if (passivePlugins.Count == 0)
    {
        logger.LogWarning("No passive probe plugins registered, rejecting webhook for target {TargetId}", targetId);
        return Results.NotFound(new { error = "No passive probe plugin registered" });
    }

    // webhook_secret comes from the cached spec snapshot (the executor has no DB). Find a passive
    // assignment for this host and read its merged config.
    string? webhookSecret = null;
    if (Guid.TryParse(targetId, out var hostGuid))
    {
        var specs = await configSource.GetCachedSpecsAsync(httpContext.RequestAborted);
        var passiveIds = passivePlugins.Select(p => p.Metadata.PluginId).ToHashSet(StringComparer.Ordinal);
        var spec = specs.FirstOrDefault(s => s.HostId == hostGuid && passiveIds.Contains(s.ProbePluginId));
        if (spec is not null && spec.MergedConfig.TryGetValue("webhook_secret", out var secret))
            webhookSecret = secret;
    }

    using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
    var payload = await reader.ReadToEndAsync();

    var signatureHeader = httpContext.Request.Headers["X-Webhook-Signature"].FirstOrDefault();
    if (webhookSecret is not null)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            logger.LogWarning("Webhook HMAC validation failed for target {TargetId}: signature header missing, HmacResult={HmacResult}",
                targetId, "fail");
            return Results.Json(new { error = "Missing X-Webhook-Signature header" }, statusCode: 401);
        }

        var expectedSignature = ComputeHmacSha256(webhookSecret, payload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signatureHeader),
                Encoding.UTF8.GetBytes(expectedSignature)))
        {
            logger.LogWarning("Webhook HMAC validation failed for target {TargetId}: signature mismatch, HmacResult={HmacResult}",
                targetId, "fail");
            return Results.Json(new { error = "Invalid signature" }, statusCode: 401);
        }

        logger.LogDebug("Webhook HMAC validation passed for target {TargetId}, HmacResult={HmacResult}",
            targetId, "pass");
    }
    else
    {
        logger.LogDebug("Webhook HMAC not configured for target {TargetId}, HmacResult={HmacResult}",
            targetId, "not-configured");
    }

    var registration = passivePlugins[0];
    var passivePlugin = (IPassiveProbePlugin)registration.Plugin;

    var result = await passivePlugin.HandleAsync(targetId, payload, httpContext.RequestAborted);

    var probeType = registration.Metadata.Name;
    var subject = $"probes.results.{probeType}.{targetId}";
    var message = new ProbeResultMessage(targetId, registration.Metadata.PluginId, probeType, result);

    await resultSink.PublishAsync(subject, message, httpContext.RequestAborted);

    logger.LogInformation("Webhook processed for target {TargetId}: {Status}, PayloadSizeBytes={PayloadSizeBytes}",
        targetId, result.Status, payload.Length);

    return Results.Ok(new { status = result.Status.ToString(), summary = result.Summary });
});

app.Run();

static string ComputeHmacSha256(string secret, string payload)
{
    var keyBytes = Encoding.UTF8.GetBytes(secret);
    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
    return $"sha256={Convert.ToHexStringLower(hash)}";
}
