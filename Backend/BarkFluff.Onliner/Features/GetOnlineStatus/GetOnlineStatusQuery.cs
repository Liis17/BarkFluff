using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.GetOnlineStatus;

public class GetOnlineStatusQuery : IRequest<GetOnlineStatusResponse>
{
    public List<long> UserIds { get; set; }
}