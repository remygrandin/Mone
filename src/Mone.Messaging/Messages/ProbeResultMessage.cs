using Mone.Contracts.Models;

namespace Mone.Messaging.Messages;

public sealed record ProbeResultMessage(
    string TargetId,
    string ProbeId,
    string ProbeType,
    ProbeResult Result);
