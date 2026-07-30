using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.UpdateReservedName;

public class UpdateReservedNameCommandHandler : IRequestHandler<UpdateReservedNameCommand, UpdateReservedNameResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<UpdateReservedNameCommandHandler> _logger;

    public UpdateReservedNameCommandHandler(ConfigurationStorage configurationStorage, ILogger<UpdateReservedNameCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<UpdateReservedNameResponse> Handle(UpdateReservedNameCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Переименование зарезервированного имени: {OldName} → {NewName}", request.OldName, request.NewName);

        try
        {
            await _configurationStorage.UpdateReservedNameAsync(request.OldName, request.NewName);

            return new UpdateReservedNameResponse
            {
                Success = true,
                Message = $"Имя '{request.OldName}' переименовано в '{request.NewName}'"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка переименования {OldName} → {NewName}", request.OldName, request.NewName);
            return new UpdateReservedNameResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
