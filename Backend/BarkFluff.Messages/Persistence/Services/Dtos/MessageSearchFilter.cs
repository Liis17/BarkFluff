using BarkFluff.Messages.Domain;

namespace BarkFluff.Messages.Persistence.Services.Dtos;

public sealed record MessageSearchFilter
{
    public string Text { get; init; } = string.Empty;
    public long? AuthorUserId { get; init; }
    public Guid? AuthorUserUuid { get; init; }
    public DateTime? SentFrom { get; init; }
    public DateTime? SentBefore { get; init; }
    public bool? HasAttachments { get; init; }
    public IReadOnlyList<MessageAttachmentType> AttachmentTypes { get; init; } = [];
    public DateTime? CursorSentAt { get; init; }
    public long CursorMessageId { get; init; }
    public int PageSize { get; init; } = 30;
}
