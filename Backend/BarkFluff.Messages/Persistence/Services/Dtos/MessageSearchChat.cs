namespace BarkFluff.Messages.Persistence.Services.Dtos;

public sealed record MessageSearchChat(
    Guid Id,
    string? Title,
    string? Picture,
    bool IsGroupChat,
    long? PeerUserId,
    Guid? PeerUserUuid);
