using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.DeleteBotsForOwner;

public sealed class DeleteBotsForOwnerCommand : IRequest<DeleteBotsForOwnerResponse>
{
    public long OwnerUserId { get; init; }
}
