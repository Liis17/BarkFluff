namespace Barkfluff.Docker.Control.Models;

public class OtlpSettings
{
    public const string SectionName = "Otlp";
    public string? ApiKey { get; set; }
    public bool Enabled { get; set; } = true;
}
