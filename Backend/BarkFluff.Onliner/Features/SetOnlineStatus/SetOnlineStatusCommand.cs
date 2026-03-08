using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.SetOnlineStatus;

public class SetOnlineStatusCommand : IRequest<SetOnlineStatusResponse>
{
    // Empty request - UserId берется из UserContext (JWT)
}