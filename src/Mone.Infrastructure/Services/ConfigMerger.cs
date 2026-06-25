using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mone.Infrastructure.Data;

namespace Mone.Infrastructure.Services;

public static class ConfigMerger
{
    public static async Task<Dictionary<string, string>> BuildMergedConfigAsync(
        MoneDbContext db,
        string pluginId,
        string? assignmentConfigJson,
        ILogger logger,
        CancellationToken ct)
    {
        var merged = new Dictionary<string, string>();

        try
        {
            var globalConfig = await db.PluginGlobalConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.PluginId == pluginId, ct);

            if (globalConfig?.ConfigJson is not null)
            {
                var globalDict = JsonSerializer.Deserialize<Dictionary<string, string>>(globalConfig.ConfigJson);
                if (globalDict is not null)
                {
                    foreach (var kvp in globalDict)
                        merged[kvp.Key] = kvp.Value;
                }
                logger.LogDebug("Loaded global config for plugin {PluginId} with {KeyCount} key(s)",
                    pluginId, globalDict?.Count ?? 0);
            }
            else
            {
                logger.LogDebug("No global config found for plugin {PluginId}", pluginId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load global config for plugin {PluginId}", pluginId);
        }

        if (assignmentConfigJson is not null)
        {
            var assignmentDict = JsonSerializer.Deserialize<Dictionary<string, string>>(assignmentConfigJson);
            if (assignmentDict is not null)
            {
                foreach (var kvp in assignmentDict)
                    merged[kvp.Key] = kvp.Value;
            }
        }

        // Resolve credentials if the assignment references them (SSH, SNMP, etc.)
        await ResolveCredentialsAsync(merged, db, logger, ct);

        return merged;
    }

    private static async Task ResolveCredentialsAsync(
        Dictionary<string, string> config,
        MoneDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        // Check if this config uses credentials
        if (!config.TryGetValue("credentials_id", out var credentialsId) || string.IsNullOrWhiteSpace(credentialsId))
            return; // No credentials to resolve

        try
        {
            if (!Guid.TryParse(credentialsId, out var credId))
            {
                logger.LogError("Invalid credentials_id format: {CredentialsId}", credentialsId);
                return;
            }

            var credentials = await db.Credentials
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == credId, ct);

            if (credentials is null)
            {
                logger.LogError("Credentials not found: {CredentialsId}", credentialsId);
                return;
            }

            var authType = config.TryGetValue("auth_type", out var auth) ? auth : "password";

            // Inject resolved credentials into the config
            config["ssh_username"] = credentials.Username;
            config["ssh_password_or_key"] = credentials.Password;
            config["ssh_auth_type"] = authType;

            logger.LogDebug("Resolved credentials {CredentialsId} for auth_type={AuthType}", credentialsId, authType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve credentials {CredentialsId}", credentialsId);
        }
    }
}
