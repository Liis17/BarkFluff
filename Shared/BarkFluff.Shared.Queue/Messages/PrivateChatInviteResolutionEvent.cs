namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Ответ приглашённого на приватный чат: принято или отклонено.
/// Updates пушит в стрим SubscribePrivateChatInviteResolutions инициатора.
/// </summary>
public class PrivateChatInviteResolutionEvent
{
    public Guid ChatId { get; set; }

    public long InviterUserId { get; set; }

    public long InviteeUserId { get; set; }

    public bool Accepted { get; set; }
}
