using Mone.Contracts.Models;

namespace Mone.Messaging.Messages;

public sealed record StatusChangeMessage(
    string CheckerId,
    StatusChange Change);
