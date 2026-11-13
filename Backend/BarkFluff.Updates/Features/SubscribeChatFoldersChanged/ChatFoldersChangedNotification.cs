namespace BarkFluff.Updates.Features.SubscribeChatFoldersChanged;

using MediatR;

public record ChatFoldersChangedNotification(long UserId) : INotification;
