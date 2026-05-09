namespace BarkFluff.Updates.Features.SubscribePrivateMessageDeletes;

using MediatR;

public record EncryptedMessageDeletedNotification(Guid ChatId, long MessageId, List<long> Members) : INotification;
