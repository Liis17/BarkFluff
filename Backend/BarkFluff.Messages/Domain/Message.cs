using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class Message
{
    [Key]
    public long Id { get; set; }

    // NULL для импортированных fed-сообщений (этап 2.3) — автор на другой ноде, см. SenderUuid.
    public long? SenderId { get; set; }

    public List<long> ReadBy { get; set; }

    public Guid ChatId { get; set; }

    public DateTime SentAt { get; set; }

    public MessageContent? Content { get; set; }

    public MessageContentType Type { get; set; }

    public bool IsDeleted { get; set; }

    public bool IsEdited { get; set; }

    public DateTime? EditedAt { get; set; }

    public DateTime LastChangeAt { get; set; }

    public Guid? FederatedId { get; set; }

    public Guid? SenderUuid { get; set; }
}