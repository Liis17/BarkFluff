using BarkFluff.Configuration.Features.GetConfiguration;
using BarkFluff.Configuration.Features.UpdateConfiguration;
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

    public override async Task<UpdateConfigurationResponse> UpdateConfiguration(UpdateConfigurationRequest request, ServerCallContext context)
    {
        _metrics.Increment("config_update_requests");
        
        var command = new UpdateConfigurationCommand
        {
            Section = request.Section,
            Key = request.Key,
            Value = request.Value,
            ServiceId = request.ServiceId,
            EditedBy = request.EditedBy,
            EditedFrom = request.EditedFrom
        };

        return await _mediator.Send(command);
    }
}