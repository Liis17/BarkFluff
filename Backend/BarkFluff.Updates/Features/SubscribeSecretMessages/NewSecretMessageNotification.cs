namespace BarkFluff.Updates.Features.SubscribeSecretMessages;

using MediatR;

public record NewSecretMessageNotification(
    string MessageId,
    long SenderUserId,
    Guid SenderDeviceId,
    long RecipientUserId,
    Guid RecipientDeviceId,
    byte[] Envelope,
    DateTime SentAt) : INotification;
