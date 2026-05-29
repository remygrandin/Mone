namespace Mone.Messaging.Messages;

public sealed record PluginReloadMessage(PluginReloadAction Action, string PluginName);

public enum PluginReloadAction { Install, Uninstall }
