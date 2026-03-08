using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Identity.Domain;

public class UserPassword
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    public string? PasswordHash { get; set; }

    public DateTime ChangedAt { get; set; }
}