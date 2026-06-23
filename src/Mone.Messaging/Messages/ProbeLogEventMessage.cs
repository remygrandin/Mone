namespace Mone.Messaging.Messages;

public sealed record ProbeLogEventMessage(
    string TargetId,
    string ProbeId,
    string ProbeType,
    string Line,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object>? Metadata = null);
