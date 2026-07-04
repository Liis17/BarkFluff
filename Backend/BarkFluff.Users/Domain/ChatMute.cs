using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class ChatMute
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    public Guid ChatId { get; set; }

    // null = замьючено навсегда; иначе — до этого момента (UTC).
    public DateTime? MutedUntil { get; set; }
}
