using BarkFluff.Configuration.Features.AddReservedName;
using BarkFluff.Configuration.Features.DeleteReservedName;
using BarkFluff.Configuration.Features.GetAllConfigurations;
using BarkFluff.Configuration.Features.GetConfiguration;
using BarkFluff.Configuration.Features.GetConfigurationHistory;
using BarkFluff.Configuration.Features.GetReservedNames;
using BarkFluff.Configuration.Features.RollbackConfiguration;
using BarkFluff.Configuration.Features.UpdateConfiguration;
using BarkFluff.Configuration.Features.UpdateReservedName;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using System.Diagnostics;

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

    public override async Task<GetConfigurationResponse> GetConfiguration(GetConfigurationRequest request, ServerCallContext context)
    {
        _metrics.Increment("config_get_requests");
        _metrics.Set("last_config_get_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new GetConfigurationCommand
            {
                ServiceId = (ServiceId)request.ServiceId
            });

            sw.Stop();
            _metrics.Increment("config_get_success");
            _metrics.Add("config_get_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics.Set("last_config_get_items", response.Configurations.Count);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("config_get_errors");
            _metrics.Add("config_get_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task<GetAllConfigurationsResponse> GetAllConfigurations(GetAllConfigurationsRequest request, ServerCallContext context)
    {
        _metrics.Increment("config_get_all_requests");
        _metrics.Set("last_config_get_all_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new GetAllConfigurationsCommand());

            sw.Stop();
            _metrics.Increment("config_get_all_success");
            _metrics.Add("config_get_all_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics.Set("last_config_get_all_items", response.Configurations.Count);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("config_get_all_errors");
            _metrics.Add("config_get_all_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task<UpdateConfigurationResponse> UpdateConfiguration(UpdateConfigurationRequest request, ServerCallContext context)
    {
        _metrics.Increment("config_update_requests");
        _metrics.Set("last_config_update_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new UpdateConfigurationCommand
            {
                Section = request.Section,
                Key = request.Key,
                Value = request.Value,
                ServiceId = request.ServiceId,
                EditedBy = request.EditedBy,
                EditedFrom = request.EditedFrom
            });

            sw.Stop();
            // handler ловит исключения сам и возвращает Success=false — учитываем это
            if (response.Success)
                _metrics.Increment("config_update_success");
            else
                _metrics.Increment("config_update_errors");

            _metrics.Add("config_update_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("config_update_errors");
            _metrics.Add("config_update_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task<GetConfigurationHistoryResponse> GetConfigurationHistory(
        GetConfigurationHistoryRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("config_history_requests");
        try
        {
            var response = await _mediator.Send(new GetConfigurationHistoryCommand
            {
                Section = request.Section,
                Key = request.Key,
                ServiceId = request.ServiceId,
                Count = request.Count
            });
            _metrics.Increment("config_history_success");
            return response;
        }
        catch
        {
            _metrics.Increment("config_history_errors");
            throw;
        }
    }

    public override async Task<RollbackConfigurationResponse> RollbackConfiguration(
        RollbackConfigurationRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("config_rollback_requests");
        var response = await _mediator.Send(new RollbackConfigurationCommand
        {
            RevisionId = request.RevisionId,
            EditedBy = request.EditedBy,
            EditedFrom = request.EditedFrom
        });

        _metrics.Increment(response.Success ? "config_rollback_success" : "config_rollback_errors");
        return response;
    }

    // ─── Reserved Names ─────────────────────────────────────────────────────────

    public override async Task<GetReservedNamesResponse> GetReservedNames(GetReservedNamesRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_get_requests");
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new GetReservedNamesCommand());
            sw.Stop();
            _metrics.Increment("reserved_names_get_success");
            _metrics.Add("reserved_names_get_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("reserved_names_get_errors");
            _metrics.Add("reserved_names_get_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task<AddReservedNameResponse> AddReservedName(AddReservedNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_add_requests");
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new AddReservedNameCommand { Name = request.Name });
            sw.Stop();
            if (response.Success)
                _metrics.Increment("reserved_names_add_success");
            else
                _metrics.Increment("reserved_names_add_errors");
            _metrics.Add("reserved_names_add_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("reserved_names_add_errors");
            _metrics.Add("reserved_names_add_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task<UpdateReservedNameResponse> UpdateReservedName(UpdateReservedNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_update_requests");
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new UpdateReservedNameCommand { OldName = request.OldName, NewName = request.NewName });
            sw.Stop();
            if (response.Success)
                _metrics.Increment("reserved_names_update_success");
            else
                _metrics.Increment("reserved_names_update_errors");
            _metrics.Add("reserved_names_update_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("reserved_names_update_errors");
            _metrics.Add("reserved_names_update_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task<DeleteReservedNameResponse> DeleteReservedName(DeleteReservedNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("reserved_names_delete_requests");
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _mediator.Send(new DeleteReservedNameCommand { Name = request.Name });
            sw.Stop();
            if (response.Success)
                _metrics.Increment("reserved_names_delete_success");
            else
                _metrics.Increment("reserved_names_delete_errors");
            _metrics.Add("reserved_names_delete_duration_ms_total", sw.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            sw.Stop();
            _metrics.Increment("reserved_names_delete_errors");
            _metrics.Add("reserved_names_delete_duration_ms_total", sw.ElapsedMilliseconds);
            throw;
        }
    }
}
