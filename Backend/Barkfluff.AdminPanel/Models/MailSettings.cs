namespace Barkfluff.AdminPanel.Models;

public class MailSettings
{
    public const string SectionName = "Mail";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;

    public List<MailAccountSettings> Accounts { get; set; } = new();
}

public class MailAccountSettings
{
    public string Address { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
