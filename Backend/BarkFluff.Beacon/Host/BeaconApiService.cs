using BarkFluff.Beacon.Features.GetServerInfo;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Beacon;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Beacon.Host;

public class BeaconApiService : BarkFluff.Proto.Beacon.BeaconApi.BeaconApiBase
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public BeaconApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public override Task<GetServerInfoResponse> GetServerInfo(GetServerInfoRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_info_requests");
        var command = new GetServerInfoCommand();

        return _mediator.Send(command);
    }
}