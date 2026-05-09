using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class PinnedMessage
{
    [Key]
    public long Id { get; set; }

    public Guid ChatId { get; set; }

    public long MessageId { get; set; }

    public long PinnerUserId { get; set; }

    public DateTime PinnedAt { get; set; }
}
