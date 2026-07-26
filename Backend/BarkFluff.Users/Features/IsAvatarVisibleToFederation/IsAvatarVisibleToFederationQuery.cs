using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.IsAvatarVisibleToFederation;

public class IsAvatarVisibleToFederationQuery : IRequest<IsAvatarVisibleToFederationResponse>
{
    public required long UserId { get; init; }
}
