using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.CheckRemoteAvatarRef;

public class CheckRemoteAvatarRefQuery : IRequest<CheckRemoteAvatarRefResponse>
{
    public required string ServerName { get; init; }

    public required string FileId { get; init; }
}
