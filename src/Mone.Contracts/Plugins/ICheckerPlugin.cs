using Mone.Contracts.Models;

namespace Mone.Contracts.Plugins;

public interface ICheckerPlugin : IPlugin
{
    CheckerInvocationMode InvocationMode { get; }

    TimeSpan? Interval { get; }

    Task<StatusChange?> EvaluateAsync(CheckerEvaluationContext context);
}
