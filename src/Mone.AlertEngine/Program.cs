using Microsoft.EntityFrameworkCore;
using Mone.AlertEngine.Services;
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

builder.Services.AddHostedService<AlertDispatcherService>();

var host = builder.Build();

var pluginEngine = host.Services.GetRequiredService<Mone.PluginEngine.PluginEngine>();
var config = host.Services.GetRequiredService<IConfiguration>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mone.AlertEngine");

var pluginDir = config["AlertEngine:PluginDirectory"] ?? "plugins";
var fullPath = Path.GetFullPath(pluginDir);
logger.LogInformation("Loading notification plugins from {PluginDirectory}", fullPath);
pluginEngine.LoadPluginsFromDirectory(fullPath);

var notificationCount = pluginEngine.Registry.CountByKind(Mone.PluginEngine.PluginKind.Notification);
logger.LogInformation("Loaded {NotificationCount} notification plugin(s)", notificationCount);

host.Run();
