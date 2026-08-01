using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

/// <summary>
/// Персональное переопределение фона для одного чата.
/// </summary>
public class UserChatSettings
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    public Guid ChatId { get; set; }

    public string ChatBackgroundFileId { get; set; } = string.Empty;
}
