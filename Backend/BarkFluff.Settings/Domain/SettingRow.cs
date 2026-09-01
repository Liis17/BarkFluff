namespace BarkFluff.Settings.Domain;

public sealed class SettingRow
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string EditedBy { get; set; } = string.Empty;

    public DateTime EditedAt { get; set; }
}
