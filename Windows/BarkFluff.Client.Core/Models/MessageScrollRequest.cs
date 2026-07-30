namespace BarkFluff.Client.Core.Models;

public enum MessageScrollTarget
{
    Bottom,
    Message
}

public sealed record MessageScrollRequest(MessageScrollTarget Target, long? MessageId = null);
