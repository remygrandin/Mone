using Mone.Messaging.Extensions;
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
builder.Services.AddHostedService<PassiveProbeHostService>();
builder.Services.AddHostedService<Mone.PluginEngine.PluginReloadListener>();

var app = builder.Build();

// Passive probes own their own listeners (see PassiveProbeHostService); the executor no longer
// hosts a shared webhook endpoint. This app stays a WebApplication only for future health/diagnostic
// endpoints and the hosted-service lifetime.
app.Run();
