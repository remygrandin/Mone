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

        return merged;
    }
}
