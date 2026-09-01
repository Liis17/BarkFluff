extern alias GrpcServer;

using System.Diagnostics;

using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Features.AddReservedName;
using BarkFluff.Settings.Features.DeleteReservedName;
using BarkFluff.Settings.Features.GetAllConfigurations;
using BarkFluff.Settings.Features.GetConfiguration;
using BarkFluff.Settings.Features.GetConfigurationHistory;
using BarkFluff.Settings.Features.GetReservedNames;
using BarkFluff.Settings.Features.RollbackConfiguration;
using BarkFluff.Settings.Features.UpdateConfiguration;
using BarkFluff.Settings.Features.UpdateReservedName;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using MetricsCollector = GrpcServer::BarkFluff.GrpcServer.Metrics.MetricsCollector;

namespace BarkFluff.Settings.Host;

public sealed class SettingsApiService : ConfigurationApi.ConfigurationApiBase
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public SettingsApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public override Task<GetConfigurationResponse> GetConfiguration(GetConfigurationRequest request, ServerCallContext context)
    {
        _metrics.Set("last_config_get_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return Measure(
            "config_get",
            () => _mediator.Send(new GetConfigurationQuery((ServiceId)request.ServiceId), context.CancellationToken),
            observe: response => _metrics.Set("last_config_get_items", response.Configurations.Count));
    }

    public override Task<GetAllConfigurationsResponse> GetAllConfigurations(GetAllConfigurationsRequest request, ServerCallContext context)
    {
        _metrics.Set("last_config_get_all_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return Measure(
            "config_get_all",
            () => _mediator.Send(new GetAllConfigurationsQuery(), context.CancellationToken),
            observe: response => _metrics.Set("last_config_get_all_items", response.Configurations.Count));
    }

    public override Task<UpdateConfigurationResponse> UpdateConfiguration(UpdateConfigurationRequest request, ServerCallContext context)
    {
        _metrics.Set("last_config_update_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return Measure("config_update", () => _mediator.Send(new UpdateConfigurationCommand(request.Section, request.Key, request.Value, request.ServiceId, request.EditedBy, request.EditedFrom), context.CancellationToken), response => response.Success);
    }

    public override Task<GetConfigurationHistoryResponse> GetConfigurationHistory(GetConfigurationHistoryRequest request, ServerCallContext context) =>
        Measure("config_history", () => _mediator.Send(new GetConfigurationHistoryQuery(request.Section, request.Key, request.ServiceId, request.Count), context.CancellationToken));

    public override Task<RollbackConfigurationResponse> RollbackConfiguration(RollbackConfigurationRequest request, ServerCallContext context) =>
        Measure("config_rollback", () => _mediator.Send(new RollbackConfigurationCommand(request.RevisionId, request.EditedBy, request.EditedFrom), context.CancellationToken), response => response.Success);

    public override Task<GetReservedNamesResponse> GetReservedNames(GetReservedNamesRequest request, ServerCallContext context) =>
        Measure("reserved_names_get", () => _mediator.Send(new GetReservedNamesQuery(), context.CancellationToken));

    public override Task<AddReservedNameResponse> AddReservedName(AddReservedNameRequest request, ServerCallContext context) =>
        Measure("reserved_names_add", () => _mediator.Send(new AddReservedNameCommand(request.Name), context.CancellationToken), response => response.Success);

    public override Task<UpdateReservedNameResponse> UpdateReservedName(UpdateReservedNameRequest request, ServerCallContext context) =>
        Measure("reserved_names_update", () => _mediator.Send(new UpdateReservedNameCommand(request.OldName, request.NewName), context.CancellationToken), response => response.Success);

    public override Task<DeleteReservedNameResponse> DeleteReservedName(DeleteReservedNameRequest request, ServerCallContext context) =>
        Measure("reserved_names_delete", () => _mediator.Send(new DeleteReservedNameCommand(request.Name), context.CancellationToken), response => response.Success);

    private async Task<T> Measure<T>(string prefix, Func<Task<T>> action, Func<T, bool>? success = null, Action<T>? observe = null)
    {
        _metrics.Increment($"{prefix}_requests");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await action();
            observe?.Invoke(response);
            _metrics.Increment(success is null || success(response) ? $"{prefix}_success" : $"{prefix}_errors");
            return response;
        }
        catch
        {
            _metrics.Increment($"{prefix}_errors");
            throw;
        }
        finally
        {
            _metrics.Add($"{prefix}_duration_ms_total", stopwatch.ElapsedMilliseconds);
        }
    }
}
