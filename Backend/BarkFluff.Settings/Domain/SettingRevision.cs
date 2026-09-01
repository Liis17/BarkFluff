namespace BarkFluff.Settings.Domain;

public sealed class SettingRevision
{
    public long Id { get; set; }

    public string SettingsTable { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string PreviousValue { get; set; } = string.Empty;

    public string NewValue { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    public string ChangedBy { get; set; } = string.Empty;

    public string ChangedFrom { get; set; } = string.Empty;

    public string ChangeKind { get; set; } = string.Empty;

    public long? SourceRevisionId { get; set; }
}
