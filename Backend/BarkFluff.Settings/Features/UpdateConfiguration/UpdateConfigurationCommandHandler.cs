using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using BarkFluff.Shared.Identity;
using MediatR;

namespace BarkFluff.Settings.Features.UpdateConfiguration;

public sealed class UpdateConfigurationCommandHandler(SettingsStorage storage, ILogger<UpdateConfigurationCommandHandler> logger)
    : IRequestHandler<UpdateConfigurationCommand, UpdateConfigurationResponse>
{
    public async Task<UpdateConfigurationResponse> Handle(UpdateConfigurationCommand request, CancellationToken cancellationToken)
    {
        if (!System.Enum.IsDefined(typeof(ServiceId), request.ServiceId))
            return new UpdateConfigurationResponse { Success = false, Message = $"Неизвестный ServiceId: {request.ServiceId}" };
        try
        {
            await storage.UpdateAsync(request.Section, request.Key, request.Value, (ServiceId)request.ServiceId, request.EditedBy, request.EditedFrom, cancellationToken);
            return new UpdateConfigurationResponse { Success = true, Message = $"Конфигурация {request.Section}.{request.Key} успешно обновлена" };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to update setting {Section}.{Key} for {ServiceId}", request.Section, request.Key, request.ServiceId);
            return new UpdateConfigurationResponse { Success = false, Message = $"Ошибка обновления конфигурации: {exception.Message}" };
        }
    }
}
