namespace BarkFluff.Updates.Features.SubscribePrivateMessages;

using BarkFluff.Proto.Shared;

using MediatR;

public record NewEncryptedMessageNotification(EncryptedMessage Message, List<long> Members, Guid ChatId) : INotification;
