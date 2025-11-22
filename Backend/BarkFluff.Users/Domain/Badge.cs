using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class Badge
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; }

    public string? Description { get; set; }

    [Required]
    [StringLength(255)]
    public string ImageUrl { get; set; }

    public DateTime CreatedDate { get; set; }

    public bool IsActive { get; set; }

    public ICollection<UserBadge> UserBadges { get; set; } = new List<UserBadge>();
}