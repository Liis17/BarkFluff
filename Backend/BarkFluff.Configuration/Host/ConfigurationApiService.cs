using BarkFluff.Configuration.Features.AddReservedName;
using BarkFluff.Configuration.Features.DeleteReservedName;
using BarkFluff.Configuration.Features.GetConfiguration;
using BarkFluff.Configuration.Features.GetReservedNames;
using BarkFluff.Configuration.Features.UpdateConfiguration;
using BarkFluff.Configuration.Features.UpdateReservedName;
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

    // ─── Reserved Names ─────────────────────────────────────────────────────────

    public override async Task<GetReservedNamesResponse> GetReservedNames(GetReservedNamesRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_get_requests");
        return await _mediator.Send(new GetReservedNamesCommand());
    }

    public override async Task<AddReservedNameResponse> AddReservedName(AddReservedNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_add_requests");
        return await _mediator.Send(new AddReservedNameCommand { Name = request.Name });
    }

    public override async Task<UpdateReservedNameResponse> UpdateReservedName(UpdateReservedNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_update_requests");
        return await _mediator.Send(new UpdateReservedNameCommand { OldName = request.OldName, NewName = request.NewName });
    }

    public override async Task<DeleteReservedNameResponse> DeleteReservedName(DeleteReservedNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_delete_requests");
        return await _mediator.Send(new DeleteReservedNameCommand { Name = request.Name });
    }
}