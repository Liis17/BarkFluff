using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class UserBadge
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; }

    public int BadgeId { get; set; }

    public Badge Badge { get; set; }

    public int Priority { get; set; } = 1000;

    public DateTime AssignedDate { get; set; }
}