using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Identity.Domain;

public class ConfirmationCode
{
    [Key]
    public Guid Id { get; set; }

    public string Value { get; set; }

    public DateTime Expires { get; set; }

    public long? OwnerId { get; set; }

    public ConfirmationCodeType Type { get; set; }
}