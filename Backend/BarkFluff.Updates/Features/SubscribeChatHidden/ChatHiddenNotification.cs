namespace BarkFluff.Updates.Features.SubscribeChatHidden;

using MediatR;

public record ChatHiddenNotification(Guid ChatId, List<long> Members, bool ContentWiped) : INotification;
