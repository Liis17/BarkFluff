using BarkFluff.Configuration.Infrastructure;
using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.DeleteReservedName;

public class DeleteReservedNameCommandHandler : IRequestHandler<DeleteReservedNameCommand, DeleteReservedNameResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<DeleteReservedNameCommandHandler> _logger;

    public DeleteReservedNameCommandHandler(ConfigurationStorage configurationStorage, ILogger<DeleteReservedNameCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<DeleteReservedNameResponse> Handle(DeleteReservedNameCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Удаление зарезервированного имени: {Name}", request.Name);

        try
        {
            await _configurationStorage.DeleteReservedNameAsync(request.Name);

            return new DeleteReservedNameResponse
            {
                Success = true,
                Message = $"Имя '{request.Name}' удалено из зарезервированных"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка удаления зарезервированного имени {Name}", request.Name);
            return new DeleteReservedNameResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
