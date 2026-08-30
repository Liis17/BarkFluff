using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Shared.Identity;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Settings.Features;

public sealed record GetConfigurationQuery(ServiceId ServiceId) : IRequest<GetConfigurationResponse>;
public sealed record GetAllConfigurationsQuery : IRequest<GetAllConfigurationsResponse>;
public sealed record UpdateConfigurationCommand(string Section, string Key, string Value, int ServiceId, string EditedBy, string EditedFrom) : IRequest<UpdateConfigurationResponse>;
public sealed record GetConfigurationHistoryQuery(string Section, string Key, int ServiceId, int Count) : IRequest<GetConfigurationHistoryResponse>;
public sealed record RollbackConfigurationCommand(long RevisionId, string EditedBy, string EditedFrom) : IRequest<RollbackConfigurationResponse>;
public sealed record GetReservedNamesQuery : IRequest<GetReservedNamesResponse>;
public sealed record AddReservedNameCommand(string Name) : IRequest<AddReservedNameResponse>;
public sealed record UpdateReservedNameCommand(string OldName, string NewName) : IRequest<UpdateReservedNameResponse>;
public sealed record DeleteReservedNameCommand(string Name) : IRequest<DeleteReservedNameResponse>;

public sealed class ConfigurationRequestHandlers :
    IRequestHandler<GetConfigurationQuery, GetConfigurationResponse>,
    IRequestHandler<GetAllConfigurationsQuery, GetAllConfigurationsResponse>,
    IRequestHandler<UpdateConfigurationCommand, UpdateConfigurationResponse>,
    IRequestHandler<GetConfigurationHistoryQuery, GetConfigurationHistoryResponse>,
    IRequestHandler<RollbackConfigurationCommand, RollbackConfigurationResponse>,
    IRequestHandler<GetReservedNamesQuery, GetReservedNamesResponse>,
    IRequestHandler<AddReservedNameCommand, AddReservedNameResponse>,
    IRequestHandler<UpdateReservedNameCommand, UpdateReservedNameResponse>,
    IRequestHandler<DeleteReservedNameCommand, DeleteReservedNameResponse>
{
    private readonly SettingsStorage _storage;
    private readonly ILogger<ConfigurationRequestHandlers> _logger;

    public ConfigurationRequestHandlers(SettingsStorage storage, ILogger<ConfigurationRequestHandlers> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<GetConfigurationResponse> Handle(GetConfigurationQuery request, CancellationToken cancellationToken)
    {
        var response = new GetConfigurationResponse();
        response.Configurations.AddRange((await _storage.GetConfigurationAsync(request.ServiceId, cancellationToken)).Select(ToProto));
        return response;
    }

    public async Task<GetAllConfigurationsResponse> Handle(GetAllConfigurationsQuery request, CancellationToken cancellationToken)
    {
        var response = new GetAllConfigurationsResponse();
        response.Configurations.AddRange((await _storage.GetAllAsync(cancellationToken)).Select(ToProto));
        return response;
    }

    public async Task<UpdateConfigurationResponse> Handle(UpdateConfigurationCommand request, CancellationToken cancellationToken)
    {
        if (!System.Enum.IsDefined(typeof(ServiceId), request.ServiceId))
            return new UpdateConfigurationResponse { Success = false, Message = $"Неизвестный ServiceId: {request.ServiceId}" };
        try
        {
            await _storage.UpdateAsync(request.Section, request.Key, request.Value, (ServiceId)request.ServiceId, request.EditedBy, request.EditedFrom, cancellationToken);
            return new UpdateConfigurationResponse { Success = true, Message = $"Конфигурация {request.Section}.{request.Key} успешно обновлена" };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update setting {Section}.{Key} for {ServiceId}", request.Section, request.Key, request.ServiceId);
            return new UpdateConfigurationResponse { Success = false, Message = $"Ошибка обновления конфигурации: {exception.Message}" };
        }
    }

    public async Task<GetConfigurationHistoryResponse> Handle(GetConfigurationHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!System.Enum.IsDefined(typeof(ServiceId), request.ServiceId))
            throw new ArgumentException($"Неизвестный ServiceId: {request.ServiceId}");
        var serviceId = (ServiceId)request.ServiceId;
        var response = new GetConfigurationHistoryResponse();
        response.Revisions.AddRange((await _storage.GetHistoryAsync(request.Section, request.Key, serviceId, request.Count, cancellationToken))
            .Select(revision => ToProto(revision, serviceId)));
        return response;
    }

    public async Task<RollbackConfigurationResponse> Handle(RollbackConfigurationCommand request, CancellationToken cancellationToken)
    {
        if (request.RevisionId <= 0)
            return new RollbackConfigurationResponse { Success = false, Message = "Некорректный идентификатор ревизии" };
        try
        {
            await _storage.RollbackAsync(request.RevisionId, request.EditedBy, request.EditedFrom, cancellationToken);
            return new RollbackConfigurationResponse { Success = true, Message = $"Изменение #{request.RevisionId} успешно откачено" };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to rollback settings revision {RevisionId}", request.RevisionId);
            return new RollbackConfigurationResponse { Success = false, Message = exception.Message };
        }
    }

    public async Task<GetReservedNamesResponse> Handle(GetReservedNamesQuery request, CancellationToken cancellationToken)
    {
        var response = new GetReservedNamesResponse();
        response.Names.AddRange(await _storage.GetReservedNamesAsync(cancellationToken));
        return response;
    }

    public async Task<AddReservedNameResponse> Handle(AddReservedNameCommand request, CancellationToken cancellationToken) =>
        await ReservedMutation(
            () => _storage.AddReservedNameAsync(request.Name, cancellationToken),
            () => new AddReservedNameResponse { Success = true, Message = $"Имя '{request.Name}' добавлено в зарезервированные" },
            message => new AddReservedNameResponse { Success = false, Message = message });

    public async Task<UpdateReservedNameResponse> Handle(UpdateReservedNameCommand request, CancellationToken cancellationToken) =>
        await ReservedMutation(
            () => _storage.UpdateReservedNameAsync(request.OldName, request.NewName, cancellationToken),
            () => new UpdateReservedNameResponse { Success = true, Message = $"Имя '{request.OldName}' переименовано в '{request.NewName}'" },
            message => new UpdateReservedNameResponse { Success = false, Message = message });

    public async Task<DeleteReservedNameResponse> Handle(DeleteReservedNameCommand request, CancellationToken cancellationToken) =>
        await ReservedMutation(
            () => _storage.DeleteReservedNameAsync(request.Name, cancellationToken),
            () => new DeleteReservedNameResponse { Success = true, Message = $"Имя '{request.Name}' удалено из зарезервированных" },
            message => new DeleteReservedNameResponse { Success = false, Message = message });

    private static ConfigurationItem ToProto(StoredSetting setting) => new()
    {
        Section = setting.Section,
        Key = setting.Key,
        Value = setting.Value,
        EditedAt = Timestamp.FromDateTime(setting.EditedAt),
        EditedBy = setting.EditedBy,
        EditedFrom = setting.EditedFrom,
        ServiceId = (int)setting.ServiceId
    };

    private static BarkFluff.Proto.Configuration.ConfigurationRevision ToProto(SettingRevision revision, ServiceId serviceId) => new()
    {
        Id = revision.Id,
        Section = BarkFluff.Settings.Catalog.SettingsCatalog.Resolve(serviceId, revision.Key).Section,
        Key = BarkFluff.Settings.Catalog.SettingsCatalog.Resolve(serviceId, revision.Key).Key,
        ServiceId = (int)serviceId,
        PreviousValue = revision.PreviousValue,
        NewValue = revision.NewValue,
        ChangedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(revision.ChangedAt, DateTimeKind.Utc)),
        ChangedBy = revision.ChangedBy,
        ChangedFrom = revision.ChangedFrom,
        ChangeKind = revision.ChangeKind,
        SourceRevisionId = revision.SourceRevisionId ?? 0
    };

    private static async Task<TResponse> ReservedMutation<TResponse>(Func<Task> action, Func<TResponse> success, Func<string, TResponse> failure)
    {
        try { await action(); return success(); }
        catch (Exception exception) { return failure(exception.Message); }
    }
}
