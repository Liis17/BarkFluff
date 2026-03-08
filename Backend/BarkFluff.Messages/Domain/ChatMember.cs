using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class ChatMember
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public Guid ChatId { get; set; }

    public Chat Chat { get; set; }
}