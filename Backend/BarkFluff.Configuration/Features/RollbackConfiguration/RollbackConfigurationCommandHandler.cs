using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.RollbackConfiguration;

public class RollbackConfigurationCommandHandler : IRequestHandler<RollbackConfigurationCommand, RollbackConfigurationResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<RollbackConfigurationCommandHandler> _logger;

    public RollbackConfigurationCommandHandler(
        ConfigurationStorage configurationStorage,
        ILogger<RollbackConfigurationCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<RollbackConfigurationResponse> Handle(
        RollbackConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RevisionId <= 0)
            return new RollbackConfigurationResponse { Success = false, Message = "Некорректный идентификатор ревизии" };

        try
        {
            await _configurationStorage.RollbackConfigurationAsync(
                request.RevisionId,
                request.EditedBy,
                request.EditedFrom);

            _logger.LogInformation("Конфигурация откатана по ревизии {RevisionId}", request.RevisionId);
            return new RollbackConfigurationResponse
            {
                Success = true,
                Message = $"Изменение #{request.RevisionId} успешно откачено"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка отката конфигурации по ревизии {RevisionId}", request.RevisionId);
            return new RollbackConfigurationResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
