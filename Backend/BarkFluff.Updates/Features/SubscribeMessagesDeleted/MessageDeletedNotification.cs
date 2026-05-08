namespace BarkFluff.Updates.Features.SubscribeMessagesDeleted;

using MediatR;

public record MessageDeletedNotification(long MessageId, List<long> Members, Guid ChatId) : INotification;
