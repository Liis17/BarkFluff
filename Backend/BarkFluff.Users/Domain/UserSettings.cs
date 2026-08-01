using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

/// <summary>
/// Синхронизируемые настройки внешнего вида пользователя.
/// </summary>
public class UserSettings
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// FileId глобального фона чатов. null означает отсутствие изображения.
    /// </summary>
    public string? GlobalChatBackgroundFileId { get; set; }
}
