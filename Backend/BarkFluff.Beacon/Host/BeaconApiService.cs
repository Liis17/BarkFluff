using BarkFluff.Beacon.Features.GetServerInfo;
using BarkFluff.Proto.Beacon;
using Grpc.Core;
using MediatR;

namespace BarkFluff.Beacon.Host;

public class BeaconApiService : BarkFluff.Proto.Beacon.BeaconApi.BeaconApiBase
{
    private readonly IMediator _mediator;

    public BeaconApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<GetServerInfoResponse> GetServerInfo(GetServerInfoRequest request, ServerCallContext context)
    {
        var command = new GetServerInfoCommand();

        return _mediator.Send(command);
    }
}