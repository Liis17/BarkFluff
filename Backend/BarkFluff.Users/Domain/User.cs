using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class User
{
    [Key]
    public long Id { get; set; }

    public Guid Uuid { get; set; }

    public string Username { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }

    public DateTime RegistrationDate { get; set; }

    public UserContact? Contact { get; set; }

    public bool IsDraft { get; set; }

    public bool IsBot { get; set; }

    public string? ProfilePicture { get; set; }

    public string? Bio { get; set; }

    public string? ProfilePicturePreviewUrl { get; set; }

    public int StorageLimitGb { get; set; } = 1;

    /// <summary>
    /// Принятая редакция Пользовательского соглашения и Политики конфиденциальности —
    /// дата «Последнее обновление» из шапки документа. null = согласие не фиксировалось.
    /// </summary>
    public string? AcceptedLegalRevision { get; set; }

    public DateTime? AcceptedLegalAt { get; set; }
}