namespace BarkFluff.Settings.Domain;

public sealed class SetupState
{
    public int Id { get; set; }

    public string CatalogFingerprint { get; set; } = string.Empty;

    public DateTime CompletedAtUtc { get; set; }

    public string CompletedBy { get; set; } = string.Empty;

    public string CompletedFrom { get; set; } = string.Empty;
}
