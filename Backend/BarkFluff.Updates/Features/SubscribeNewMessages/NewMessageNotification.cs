namespace BarkFluff.Updates.Features.SubscribeNewMessages;

using BarkFluff.Proto.Shared;
using MediatR;

public record NewMessageNotification(Message Message, List<long> Members, Guid ChatId) : INotification;
