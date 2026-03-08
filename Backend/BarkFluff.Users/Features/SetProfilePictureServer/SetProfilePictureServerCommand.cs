using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.SetProfilePictureServer;

public class SetProfilePictureServerCommand : IRequest<SetProfilePictureServerResponse>
{
    public long UserId { get; init; }

    public string ProfilePictureUrl { get; init; } = string.Empty;

    public string ProfilePicturePreviewUrl { get; init; } = string.Empty;
}
