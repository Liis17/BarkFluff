namespace BarkFluff.ClientV2.WPF.Models;

public enum MessageScrollTarget
{
    Bottom,
    Message
}

public sealed record MessageScrollRequest(MessageScrollTarget Target, long? MessageId = null);
