namespace Barkfluff.AdminPanel.Models.Dtos;

public sealed record MailMessageDetailDto(
    uint Uid,
    MailAddressDto? From,
    IReadOnlyList<MailAddressDto> To,
    IReadOnlyList<MailAddressDto> Cc,
    string Subject,
    DateTimeOffset Date,
    string? MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyList<MailAttachmentDto> Attachments
);
