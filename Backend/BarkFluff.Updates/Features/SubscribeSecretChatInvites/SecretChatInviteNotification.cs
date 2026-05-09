namespace BarkFluff.Updates.Features.SubscribeSecretChatInvites;

using MediatR;

public record SecretChatInviteNotification(
    string InviteId,
    long SenderUserId,
    Guid SenderDeviceId,
    long RecipientUserId,
    Guid RecipientDeviceId,
    byte[] InitialEnvelope,
    DateTime SentAt) : INotification;
