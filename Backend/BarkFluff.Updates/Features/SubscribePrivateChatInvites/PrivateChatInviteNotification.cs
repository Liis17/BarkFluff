namespace BarkFluff.Updates.Features.SubscribePrivateChatInvites;

using MediatR;

public record PrivateChatInviteNotification(
    Guid ChatId,
    long InviterUserId,
    long InviteeUserId,
    byte[] KdfSalt,
    byte[] PassphraseVerifier,
    DateTime InvitedAt) : INotification;
