using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.SetProfilePosterServer;

public class SetProfilePosterServerCommand : IRequest<SetProfilePosterServerResponse>
{
    public long UserId { get; set; }
    public string? PosterFileId { get; set; }
}
