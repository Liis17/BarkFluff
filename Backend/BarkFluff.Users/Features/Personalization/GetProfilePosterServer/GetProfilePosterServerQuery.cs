using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetProfilePosterServer;

public class GetProfilePosterServerQuery : IRequest<GetProfilePosterServerResponse>
{
    public long UserId { get; set; }
}
