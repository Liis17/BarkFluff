namespace BarkFluff.Updates.Features.SubscribePrivateChatInviteResolutions;

using MediatR;

public record PrivateChatInviteResolutionNotification(
    Guid ChatId,
    long InviterUserId,
    long InviteeUserId,
    bool Accepted) : INotification;
