using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;

public class GetUserPrivacyServerQuery : IRequest<GetUserPrivacyResponse>
{
    public long UserId { get; set; }
}
