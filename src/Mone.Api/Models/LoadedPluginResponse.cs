using Mone.Contracts.Models;

namespace Mone.Api.Models;

public sealed record LoadedPluginResponse(
    string PluginId,
    string Name,
    string Version,
    string? InformationalVersion,
    string Description,
    string Kind,
    string? ProbeMode,
    ConfigManifest? ConfigManifest);
