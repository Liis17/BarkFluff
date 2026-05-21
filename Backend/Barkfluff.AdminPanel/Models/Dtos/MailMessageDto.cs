namespace Barkfluff.AdminPanel.Models.Dtos;

public sealed record MailMessageDto(
    uint Uid,
    MailAddressDto? From,
    IReadOnlyList<MailAddressDto> To,
    string Subject,
    DateTimeOffset Date,
    bool IsRead,
    bool HasAttachments,
    string Preview
);

public sealed record MailAddressDto(string? Name, string Address);

public sealed record MailMessageListResult(
    IReadOnlyList<MailMessageDto> Items,
    int Total,
    int Page,
    int Size
);
