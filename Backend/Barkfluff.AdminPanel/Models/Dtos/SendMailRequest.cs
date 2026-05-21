namespace Barkfluff.AdminPanel.Models.Dtos;

public sealed class SendMailRequest
{
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }

    public string? InReplyTo { get; set; }
    public List<string> References { get; set; } = new();
}
