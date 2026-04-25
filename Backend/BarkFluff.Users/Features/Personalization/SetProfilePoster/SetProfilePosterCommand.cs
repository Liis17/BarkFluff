using MediatR;

namespace BarkFluff.Users.Features.Personalization.SetProfilePoster;

public class SetProfilePosterCommand : IRequest
{
    public string? ProfilePosterFileId { get; init; }
}
