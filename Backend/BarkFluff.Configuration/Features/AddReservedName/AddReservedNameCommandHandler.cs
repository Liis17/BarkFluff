using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.AddReservedName;

public class AddReservedNameCommandHandler : IRequestHandler<AddReservedNameCommand, AddReservedNameResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<AddReservedNameCommandHandler> _logger;

    public AddReservedNameCommandHandler(ConfigurationStorage configurationStorage, ILogger<AddReservedNameCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<AddReservedNameResponse> Handle(AddReservedNameCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Добавление зарезервированного имени: {Name}", request.Name);

        try
        {
            await _configurationStorage.AddReservedNameAsync(request.Name);

            return new AddReservedNameResponse
            {
                Success = true,
                Message = $"Имя '{request.Name}' успешно зарезервировано"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка добавления зарезервированного имени {Name}", request.Name);
            return new AddReservedNameResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
