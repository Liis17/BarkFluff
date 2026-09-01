using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.RollbackConfiguration;

public sealed class RollbackConfigurationCommandHandler(SettingsStorage storage, ILogger<RollbackConfigurationCommandHandler> logger)
    : IRequestHandler<RollbackConfigurationCommand, RollbackConfigurationResponse>
{
    public async Task<RollbackConfigurationResponse> Handle(RollbackConfigurationCommand request, CancellationToken cancellationToken)
    {
        if (request.RevisionId <= 0)
            return new RollbackConfigurationResponse { Success = false, Message = "Некорректный идентификатор ревизии" };
        try
        {
            await storage.RollbackAsync(request.RevisionId, request.EditedBy, request.EditedFrom, cancellationToken);
            return new RollbackConfigurationResponse { Success = true, Message = $"Изменение #{request.RevisionId} успешно откачено" };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to rollback settings revision {RevisionId}", request.RevisionId);
            return new RollbackConfigurationResponse { Success = false, Message = exception.Message };
        }
    }
}
