using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Messaging.Extensions;
using Mone.Messaging.Messages;
using Mone.ProbeExecutor.Jobs;
using Mone.ProbeExecutor.Services;
using NATS.Client.JetStream;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MoneDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddMoneMessaging(
    builder.Configuration.GetConnectionString("Nats") ?? "nats://localhost:4222");

builder.Services.AddSingleton(sp =>
    new Mone.PluginEngine.PluginEngine(
        sp.GetRequiredService<ILogger<Mone.PluginEngine.PluginEngine>>(), enableHotReload: false));

builder.Services.AddQuartz();

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddScoped<Mone.Infrastructure.Services.InheritanceResolver>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp =>
    Mone.Infrastructure.Services.NodeIdentity.Resolve(
        sp.GetRequiredService<IConfiguration>(), Mone.Contracts.Models.ExecutorRole.Probe));
builder.Services.AddHostedService<Mone.Infrastructure.Services.NodeRegistrationService>();
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
    MoneDbContext db,
    INatsJSContext jetStream,
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

    var assignment = await db.ProbeAssignments
        .FirstOrDefaultAsync(a => a.HostId == Guid.Parse(targetId) && a.Enabled);

    string? webhookSecret = null;
    if (assignment?.ConfigJson is not null)
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(assignment.ConfigJson);
        if (config?.TryGetValue("webhook_secret", out var secretElement) == true)
            webhookSecret = secretElement.GetString();
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

    try
    {
        await jetStream.PublishAsync(subject, message);
        logger.LogDebug("Published webhook probe result to NATS subject {Subject}", subject);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to publish webhook probe result to NATS for {TargetId}", targetId);
    }

    var entity = new ProbeResultEntity
    {
        Timestamp = result.Timestamp,
        TargetId = Guid.Parse(targetId),
        ProbeId = registration.Metadata.PluginId,
        Status = result.Status,
        Summary = result.Summary,
        DurationMs = result.Duration.TotalMilliseconds,
        MetadataJson = result.Metadata is not null ? JsonSerializer.Serialize(result.Metadata) : null
    };

    try
    {
        db.ProbeResults.Add(entity);
        await db.SaveChangesAsync(httpContext.RequestAborted);
        logger.LogDebug("Stored webhook probe result in database for {TargetId}", targetId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to store webhook probe result in database for {TargetId}", targetId);
    }

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
