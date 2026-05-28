using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TestPluginWithJsonV12;

[ProbePlugin(ProbeMode = ProbeMode.Active, InstantiationMode = InstantiationMode.Batch)]
public sealed class JsonV12Plugin : IProbePlugin
{
    public string Name => "JsonV12Probe";
    public Version Version => new(1, 0, 0);
    public string Description => "Test plugin using Newtonsoft.Json 12.x for ALC isolation test";
    public ProbeMode ProbeMode => ProbeMode.Active;
    public InstantiationMode InstantiationMode => InstantiationMode.Batch;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        var obj = new JObject { ["version"] = "12.0.3", ["target"] = targetId };
        var serialized = JsonConvert.SerializeObject(obj);
        var newtonsoftVersion = typeof(JsonConvert).Assembly.GetName().Version!.ToString();

        return Task.FromResult(new ProbeResult(
            MonitoringStatus.Healthy,
            $"JsonV12: {serialized} (Newtonsoft {newtonsoftVersion})",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(1),
            new Dictionary<string, object> { ["newtonsoft_version"] = newtonsoftVersion }));
    }
}
