using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.GetFederatedProfile;

public class GetFederatedProfileQuery : IRequest<GetFederatedProfileResponse>
{
    public string? Username { get; set; }

    public Guid? Uuid { get; set; }
}
