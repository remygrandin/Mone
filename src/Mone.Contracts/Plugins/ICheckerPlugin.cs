using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface ICheckerPlugin : IPlugin
{
    CheckerMode CheckerMode { get; }
    Task<StatusChange> EvaluateAsync(string targetId, ProbeResult result, CancellationToken cancellationToken);
}
