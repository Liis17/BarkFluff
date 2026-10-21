namespace Barkfluff.AdminPanel.Models;

public class SeqSettings
{
    public const string SectionName = "Seq";
    public bool Enabled { get; set; } = true;
    public string ServerUrl { get; set; } = "http://seq:80";
    public string? ApiKey { get; set; }
}
