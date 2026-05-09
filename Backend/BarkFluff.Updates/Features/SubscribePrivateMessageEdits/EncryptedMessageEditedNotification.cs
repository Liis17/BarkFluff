namespace BarkFluff.Updates.Features.SubscribePrivateMessageEdits;

using BarkFluff.Proto.Shared;

using MediatR;

public record EncryptedMessageEditedNotification(EncryptedMessage Message, List<long> Members, Guid ChatId) : INotification;
