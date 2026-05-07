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

    public override async Task<GetServerInfoResponse> GetServerInfo(GetServerInfoRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_info_requests");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var command = new GetServerInfoCommand();
            var response = await _mediator.Send(command);
            _metrics.Increment("server_info_success");
            _metrics.Add("server_info_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics.Set("last_server_info_request_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return response;
        }
        catch
        {
            _metrics.Increment("server_info_errors");
            throw;
        }
    }
}