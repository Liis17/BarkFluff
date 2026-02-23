using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.GetActiveSessionsServer;

public class GetActiveSessionsServerCommand : IRequest<GetActiveSessionsResponse>
{
    public long UserId { get; set; }
}
