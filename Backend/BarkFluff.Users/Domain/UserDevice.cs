using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class UserDevice
{
    [Key]
    public Guid Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; }

    public string OriginalName { get; set; }

    public string? CustomName { get; set; }

    public DateTime AuthorizedAt { get; set; }

    public string? AppName { get; set; }

    public string? OperationSystem { get; set; }

    public string? Location { get; set; }

    public string? FirebaseDeviceToken { get; set; }

    public bool NotificationsEnabled { get; set; } = true;
}
