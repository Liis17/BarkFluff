using BarkFluff.Shared.Identity;

using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Configuration.Domain;

public class ConfigurationRevision
{
    [Key]
    public long Id { get; set; }

    public long ConfigurationItemId { get; set; }

    public ConfigurationItem ConfigurationItem { get; set; } = null!;

    public string Section { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public ServiceId ServiceId { get; set; }

    public string PreviousValue { get; set; } = string.Empty;

    public string NewValue { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    public string ChangedBy { get; set; } = string.Empty;

    public string ChangedFrom { get; set; } = string.Empty;

    public string ChangeKind { get; set; } = "Update";

    public long? SourceRevisionId { get; set; }
}
