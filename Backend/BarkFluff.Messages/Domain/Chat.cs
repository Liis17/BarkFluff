using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class Chat
{
    [Key]
    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Picture { get; set; }

    public bool IsGroupChat { get; set; }

    public Message? LastMessage { get; set; }

    public List<ChatMember>? Members { get; set; }

    public int CountUnread { get; set; }

    public long? FirstUnreadMessageId { get; set; }

    public ChatType Type { get; set; } = ChatType.Regular;

    public byte[]? KdfSalt { get; set; }

    public byte[]? PassphraseVerifier { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Нормализованная пара участников приватного чата. Поля намеренно живут
    // рядом с Chat: это позволяет обеспечить уникальность пары одной БД.
    public long? PrivateUserLowId { get; set; }

    public long? PrivateUserHighId { get; set; }

    public PrivateChatInviteState PrivateInviteState { get; set; } = PrivateChatInviteState.Pending;

    // Вычисляется в выборке списка чатов и не хранится в БД.
    public DateTime LastActivityAt { get; set; }

    // Инициатор приватного инвайта: пока чат не Accepted, единственный реальный
    // member — инициатор. Вычисляется в выборке, не хранится в БД.
    public long? PrivateInviterUserId { get; set; }

    // Федеративный DM (этап 2.3, docs/rearch/05-chat-replication.md). IsFederated=false — обычный
    // локальный чат, остальные поля не используются.
    public bool IsFederated { get; set; }

    public FederatedStatus FederatedStatus { get; set; } = FederatedStatus.Active;

    // Нормализованная пара UUID участников fed-DM (Low < High лексикографически, см. 2.7).
    // Уникальна для Active-чатов — анти-дубль одновременного создания.
    public Guid? FederatedUuidLow { get; set; }

    public Guid? FederatedUuidHigh { get; set; }
}
