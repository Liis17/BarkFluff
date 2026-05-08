namespace BarkFluff.Updates.Features.SubscribeMessagesEdited;

using BarkFluff.Proto.Shared;

using MediatR;

public record MessageEditedNotification(Message Message, List<long> Members, Guid ChatId) : INotification;
