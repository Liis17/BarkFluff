namespace Barkfluff.AdminPanel.Models.Dtos;

public sealed record MailAttachmentDto(
    int Index,
    string FileName,
    string MimeType,
    long Size
);
