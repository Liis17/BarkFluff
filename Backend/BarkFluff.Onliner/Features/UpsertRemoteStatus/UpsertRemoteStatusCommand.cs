using BarkFluff.Proto.Onliner;

using MediatR;

using DomainStatusTypeId = BarkFluff.Onliner.Domain.Enums.StatusTypeId;

namespace BarkFluff.Onliner.Features.UpsertRemoteStatus;

public class UpsertRemoteStatusCommand : IRequest<UpsertRemoteStatusResponse>
{
    public required Guid UserUuid { get; init; }

    public required DomainStatusTypeId Status { get; init; }

    public required DateTime LastSeen { get; init; }
}
