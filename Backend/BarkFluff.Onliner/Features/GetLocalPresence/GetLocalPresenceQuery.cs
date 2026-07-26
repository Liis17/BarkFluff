using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.GetLocalPresence;

public class GetLocalPresenceQuery : IRequest<GetLocalPresenceResponse>
{
    public required IReadOnlyList<long> UserIds { get; init; }
}
