namespace BarkFluff.Updates.Features.SubscribeMessagesPinned;

using MediatR;

public record MessagePinnedNotification(
    Guid ChatId,
    long MessageId,
    long PinnerUserId,
    DateTime PinnedAt,
    List<long> Members) : INotification;
