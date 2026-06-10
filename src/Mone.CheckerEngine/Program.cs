using Microsoft.EntityFrameworkCore;
using Mone.CheckerEngine.Services;
using Mone.Infrastructure.Data;
using Mone.Messaging.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<MoneDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddMoneMessaging(
    builder.Configuration.GetConnectionString("Nats") ?? "nats://localhost:4222");

builder.Services.AddSingleton(sp =>
    new Mone.PluginEngine.PluginEngine(
        sp.GetRequiredService<ILogger<Mone.PluginEngine.PluginEngine>>(), enableHotReload: false));

builder.Services.AddScoped<Mone.Infrastructure.Services.InheritanceResolver>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp =>
    Mone.Infrastructure.Services.NodeIdentity.Resolve(
        sp.GetRequiredService<IConfiguration>(), Mone.Contracts.Models.ExecutorRole.Checker));
builder.Services.AddHostedService<Mone.Infrastructure.Services.NodeRegistrationService>();
builder.Services.AddSingleton<StatusTracker>();
builder.Services.AddSingleton<CheckerDispatcher>();
builder.Services.AddHostedService<StreamCheckerService>();
builder.Services.AddHostedService<IntervalCheckerScheduler>();
builder.Services.AddHostedService<Mone.PluginEngine.PluginReloadListener>();

var host = builder.Build();

var pluginEngine = host.Services.GetRequiredService<Mone.PluginEngine.PluginEngine>();
var config = host.Services.GetRequiredService<IConfiguration>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mone.CheckerEngine");

var pluginDir = config["CheckerEngine:PluginDirectory"] ?? "plugins";
var fullPath = Path.GetFullPath(pluginDir);
logger.LogInformation("Loading checker plugins from {PluginDirectory}", fullPath);
pluginEngine.LoadPluginsFromDirectory(fullPath);

var checkerCount = pluginEngine.Registry.CountByKind(Mone.PluginEngine.PluginKind.Checker);
logger.LogInformation("Loaded {CheckerCount} checker plugin(s)", checkerCount);

host.Run();
