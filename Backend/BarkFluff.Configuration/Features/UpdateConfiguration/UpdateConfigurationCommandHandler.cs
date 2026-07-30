using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using MediatR;

namespace BarkFluff.Configuration.Features.UpdateConfiguration;

public class UpdateConfigurationCommandHandler : IRequestHandler<UpdateConfigurationCommand, UpdateConfigurationResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<UpdateConfigurationCommandHandler> _logger;

    public UpdateConfigurationCommandHandler(ConfigurationStorage configurationStorage, ILogger<UpdateConfigurationCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<UpdateConfigurationResponse> Handle(UpdateConfigurationCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(ServiceId), request.ServiceId))
        {
            _logger.LogWarning(
                "Отклонено обновление конфигурации {Section}.{Key}: неизвестный ServiceId={ServiceId}",
                request.Section, request.Key, request.ServiceId
            );

            return new UpdateConfigurationResponse
            {
                Success = false,
                Message = $"Неизвестный ServiceId: {request.ServiceId}"
            };
        }

        _logger.LogInformation(
            "Обновление конфигурации: {Section}.{Key} для сервиса {ServiceId}",
            request.Section, request.Key, (ServiceId)request.ServiceId
        );

        try
        {
            await _configurationStorage.UpdateConfigurationAsync(
                request.Section,
                request.Key,
                request.Value,
                (ServiceId)request.ServiceId,
                request.EditedBy,
                request.EditedFrom
            );

            _logger.LogInformation(
                "Конфигурация {Section}.{Key} успешно обновлена",
                request.Section, request.Key
            );

            return new UpdateConfigurationResponse
            {
                Success = true,
                Message = $"Конфигурация {request.Section}.{request.Key} успешно обновлена"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления конфигурации {Section}.{Key}", request.Section, request.Key);
            return new UpdateConfigurationResponse
            {
                Success = false,
                Message = $"Ошибка обновления конфигурации: {ex.Message}"
            };
        }
    }
}
