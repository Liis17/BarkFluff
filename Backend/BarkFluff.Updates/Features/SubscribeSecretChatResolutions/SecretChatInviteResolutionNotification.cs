namespace BarkFluff.Updates.Features.SubscribeSecretChatResolutions;

using MediatR;

public record SecretChatInviteResolutionNotification(
    string InviteId,
    long SenderUserId,
    Guid SenderDeviceId,
    long RecipientUserId,
    Guid RecipientDeviceId,
    bool Accepted,
    byte[] ResponseEnvelope) : INotification;
