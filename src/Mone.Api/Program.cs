using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mone.Api.Endpoints;
using Mone.Api.Services;
using Mone.Infrastructure.Data;
using Mone.Infrastructure.Data.Entities;
using Mone.Infrastructure.Services;
using Mone.Messaging.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MoneDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddIdentityCore<UserEntity>()
    .AddEntityFrameworkStores<MoneDbContext>();

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"]!;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

var oidcEnabled = builder.Configuration.GetValue<bool>("Oidc:Enabled");
if (oidcEnabled)
{
    authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.ClientId = builder.Configuration["Oidc:ClientId"];
        options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
        options.ResponseType = "code";
        options.CallbackPath = "/api/auth/oidc/signin-oidc";
        options.SaveTokens = false;
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.GetClaimsFromUserInfoEndpoint = true;
    });
    builder.Services.AddSingleton<OidcEnabledFlag>();
}

builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

builder.Services.AddMoneMessaging(
    builder.Configuration.GetConnectionString("Nats") ?? "nats://localhost:4222");

builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mone/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
});

builder.Services.AddSingleton<Mone.PluginEngine.PluginEngine>();
builder.Services.AddSingleton<IInstalledPluginQuery, PluginEngineInstalledPluginQuery>();
builder.Services.AddScoped<IPluginRepositoryService, PluginRepositoryService>();
builder.Services.AddScoped<InheritanceResolver>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<ExternalAuthProvisioningService>();
builder.Services.AddScoped<ProbeAssignmentNameValidator>();
builder.Services.AddScoped<ProbeAssignmentMetricMaterializer>();

var ldapEnabled = builder.Configuration.GetValue<bool>("Ldap:Enabled");
if (ldapEnabled)
{
    builder.Services.AddScoped<LdapAuthService>();
    builder.Services.AddSingleton<LdapEnabledFlag>();
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
    var retries = 10;
    for (var i = 0; i < retries; i++)
    {
        try
        {
            await db.Database.MigrateAsync();
            app.Logger.LogInformation("Database migration completed");
            break;
        }
        catch (Exception ex) when (i < retries - 1)
        {
            app.Logger.LogWarning(ex, "Migration attempt {Attempt}/{MaxRetries} failed, retrying in 3s...", i + 1, retries);
            await Task.Delay(3000);
        }
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>();
    if (await userManager.FindByEmailAsync("admin@mone.local") is null)
    {
        var admin = new UserEntity { UserName = "admin@mone.local", Email = "admin@mone.local" };
        var result = await userManager.CreateAsync(admin, "Admin123!");
        if (result.Succeeded)
            app.Logger.LogInformation("Default admin user created: admin@mone.local");
        else
            app.Logger.LogWarning("Failed to create default admin: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}

var pluginDir = builder.Configuration["Api:PluginDirectory"] ?? "plugins";
var pluginFullPath = Path.GetFullPath(pluginDir);
Directory.CreateDirectory(pluginFullPath);
var pluginEngine = app.Services.GetRequiredService<Mone.PluginEngine.PluginEngine>();
pluginEngine.LoadPluginsFromDirectory(pluginFullPath);
app.Logger.LogInformation("Loaded {Count} plugin(s) from {Path}",
    pluginEngine.Registry.GetAll().Count, pluginFullPath);

using (var scope = app.Services.CreateScope())
{
    try
    {
        var materializer = scope.ServiceProvider.GetRequiredService<ProbeAssignmentMetricMaterializer>();
        await materializer.MaterializeAllAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Startup probe-assignment metric re-materialization failed");
    }
}

_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MoneDbContext>();
    var svc = scope.ServiceProvider.GetRequiredService<IPluginRepositoryService>();
    var neverSynced = await db.PluginRepositories
        .Where(r => r.Enabled && r.LastSyncedAt == null)
        .Select(r => r.Id)
        .ToListAsync();
    foreach (var id in neverSynced)
    {
        try { await svc.SyncRepositoryAsync(id); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Initial sync failed for repository {RepositoryId}", id); }
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapAuthEndpoints();
app.MapAuthProviderEndpoints();
if (app.Services.GetService<OidcEnabledFlag>() is not null)
{
    app.MapOidcAuthEndpoints();
    app.Logger.LogInformation("OIDC authentication provider enabled");
}
if (app.Services.GetService<LdapEnabledFlag>() is not null)
{
    app.MapLdapAuthEndpoints();
    app.Logger.LogInformation("LDAP authentication provider enabled");
}
app.MapHostEndpoints();
app.MapTagEndpoints();
app.MapProbeAssignmentEndpoints();
app.MapCheckerAssignmentEndpoints();
app.MapStatusEndpoints();
app.MapProbeResultEndpoints();
app.MapNotificationAuditEndpoints();
app.MapNotificationConfigEndpoints();
app.MapDashboardEndpoints();
app.MapPluginRepositoryEndpoints();
app.MapLoadedPluginEndpoints();
app.MapPluginGlobalConfigEndpoints();
app.MapHostGroupEndpoints();
app.MapGroupProbeAssignmentEndpoints();
app.MapGroupCheckerAssignmentEndpoints();
app.MapEffectiveAssignmentEndpoints();
app.MapAssignmentOverrideEndpoints();
app.MapProbeTriggerEndpoints();
app.MapHousekeepingEndpoints();

app.MapFallbackToFile("index.html")
    .AllowAnonymous();

app.Run();

public partial class Program { }

public sealed class OidcEnabledFlag;

public sealed class LdapEnabledFlag;
