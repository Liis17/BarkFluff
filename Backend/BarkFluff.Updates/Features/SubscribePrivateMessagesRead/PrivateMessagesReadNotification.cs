namespace BarkFluff.Updates.Features.SubscribePrivateMessagesRead;

using MediatR;

public record PrivateMessagesReadNotification(
    Guid ChatId,
    long UserId,
    long LastReadMessageId,
    List<long> Members) : INotification;
