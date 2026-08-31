using BarkFluff.Proto.SettingsSetup;
using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Infrastructure;
using BarkFluff.Settings.Settings;

using Grpc.Core;

namespace BarkFluff.Settings.Host;

public sealed class SettingsSetupApiService : SettingsSetupApi.SettingsSetupApiBase
{
    private const string SetupTokenHeader = "x-settings-setup-token";

    private readonly SettingsSetupCoordinator _coordinator;
    private readonly SettingsSetupOptions _options;

    public SettingsSetupApiService(SettingsSetupCoordinator coordinator, SettingsSetupOptions options)
    {
        _coordinator = coordinator;
        _options = options;
    }

    public override async Task<GetSetupStateResponse> GetSetupState(
        GetSetupStateRequest request,
        ServerCallContext context)
    {
        Authorize(context);
        return ToProto(await _coordinator.GetSnapshotAsync(context.CancellationToken));
    }

    public override async Task<SaveSetupGroupResponse> SaveSetupGroup(
        SaveSetupGroupRequest request,
        ServerCallContext context)
    {
        Authorize(context);
        try
        {
            var values = request.Values
                .GroupBy(value => value.FieldId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (string?)group.Last().Value, StringComparer.Ordinal);
            var state = await _coordinator.SaveGroupAsync(
                request.GroupId,
                values,
                "setup",
                request.EditedFrom,
                context.CancellationToken);
            return new SaveSetupGroupResponse
            {
                Success = true,
                Message = "Setup group saved.",
                State = ToProto(state)
            };
        }
        catch (Exception exception) when (exception is SetupFieldValidationException or ArgumentException or KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (SetupLockedException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<CompleteSetupResponse> CompleteSetup(
        CompleteSetupRequest request,
        ServerCallContext context)
    {
        Authorize(context);
        try
        {
            var state = await _coordinator.CompleteAsync(
                "setup",
                request.CompletedFrom,
                context.CancellationToken);
            return new CompleteSetupResponse
            {
                Success = true,
                Message = "Initial setup completed.",
                State = ToProto(state)
            };
        }
        catch (SetupIncompleteException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
        catch (SetupLockedException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    private void Authorize(ServerCallContext context)
    {
        if (!_options.Enabled)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Settings setup mode is disabled."));

        var candidate = context.RequestHeaders.FirstOrDefault(item =>
            string.Equals(item.Key, SetupTokenHeader, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!_options.IsValid(candidate))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid settings setup token."));
    }

    private static GetSetupStateResponse ToProto(SetupSnapshot snapshot)
    {
        var response = new GetSetupStateResponse
        {
            Complete = snapshot.Complete,
            Locked = snapshot.Locked,
            CatalogFingerprint = snapshot.CatalogFingerprint,
            CompletedAtUtc = snapshot.CompletedAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty
        };
        response.Groups.Add(snapshot.Groups.Select(ToProto));
        return response;
    }

    private static SetupGroup ToProto(SetupGroupSnapshot group)
    {
        var proto = new SetupGroup
        {
            Id = group.Metadata.Id,
            Order = group.Metadata.Order,
            Title = group.Metadata.Title,
            Description = group.Metadata.Description,
            Applicable = group.Applicable,
            Complete = group.Complete
        };
        proto.Fields.Add(group.Fields.Select(ToProto));
        return proto;
    }

    private static SetupField ToProto(SetupFieldSnapshot field) => new()
    {
        Id = field.Id,
        ServiceId = (int)field.ServiceId,
        Section = field.Section,
        Key = field.Key,
        StorageKey = field.StorageKey,
        Label = field.Metadata.Label,
        Description = field.Metadata.Description,
        Placeholder = field.Metadata.Placeholder,
        InputType = field.Metadata.InputType.ToString(),
        Requirement = field.Metadata.Requirement.ToString(),
        ValidatorId = field.Metadata.ValidatorId,
        Sensitive = field.IsSensitive,
        Required = field.Required,
        Applicable = field.Applicable,
        Configured = field.Configured,
        Value = field.Value,
        Error = field.Error ?? string.Empty
    };
}
