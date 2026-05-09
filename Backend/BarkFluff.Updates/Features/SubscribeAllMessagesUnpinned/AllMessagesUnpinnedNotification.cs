namespace BarkFluff.Updates.Features.SubscribeAllMessagesUnpinned;

using MediatR;

public record AllMessagesUnpinnedNotification(
    Guid ChatId,
    List<long> Members) : INotification;
