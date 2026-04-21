using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class UserPersonalization
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// FileId файла-постера профиля (аватар/обложка профиля).
    /// </summary>
    public string? ProfilePosterFileId { get; set; }

    /// <summary>
    /// Массив FileId фоновых изображений чатов, загруженных пользователем.
    /// </summary>
    public string[] ChatBackgroundFileIds { get; set; } = [];
}
