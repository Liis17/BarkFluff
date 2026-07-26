using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotUserInfo;

public class GetBotUserInfoQuery : IRequest<GetUserInfoResponse>
{
    public long? UserId { get; set; }

    public string? Username { get; set; }
}
