using BarkFluff.Configuration.Features.GetConfiguration;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using MediatR;

namespace BarkFluff.Configuration.Host;

public class ConfigurationApiService : BarkFluff.Proto.Configuration.ConfigurationApi.ConfigurationApiBase
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public ConfigurationApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public override Task<GetConfigurationResponse> GetConfiguration(GetConfigurationRequest request, ServerCallContext context)
    {
        _metrics.Increment("config_requests");
        var command = new GetConfigurationCommand()
        {
            ServiceId = (ServiceId)request.ServiceId
        };

        return _mediator.Send(command);
    }
}