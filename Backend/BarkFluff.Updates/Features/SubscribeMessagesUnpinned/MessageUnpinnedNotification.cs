namespace BarkFluff.Updates.Features.SubscribeMessagesUnpinned;

using MediatR;

public record MessageUnpinnedNotification(
    Guid ChatId,
    long MessageId,
    List<long> Members) : INotification;
