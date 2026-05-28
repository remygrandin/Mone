using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface IConfigurablePlugin
{
    ConfigManifest GetConfigManifest();
}
