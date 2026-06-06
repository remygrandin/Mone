namespace Mone.Messaging.Messages;

public sealed record ProbeScheduleChangedMessage(
    string ChangeType,
    Guid AssignmentId,
    DateTimeOffset Timestamp);
