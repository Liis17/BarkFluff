namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Приглашение в приватный чат для собеседника.
/// Updates пушит в стрим SubscribePrivateChatInvites пользователя-приглашённого.
/// </summary>
public class PrivateChatInviteEvent
{
    public Guid ChatId { get; set; }

    public long InviterUserId { get; set; }

    public long InviteeUserId { get; set; }

    public byte[] KdfSalt { get; set; } = Array.Empty<byte>();

    public byte[] PassphraseVerifier { get; set; } = Array.Empty<byte>();

    public DateTime InvitedAt { get; set; }
}
