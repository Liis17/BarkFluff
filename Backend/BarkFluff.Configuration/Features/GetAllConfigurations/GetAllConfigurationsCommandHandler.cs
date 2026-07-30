using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Configuration.Features.GetAllConfigurations;

public class GetAllConfigurationsCommandHandler : IRequestHandler<GetAllConfigurationsCommand, GetAllConfigurationsResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<GetAllConfigurationsCommandHandler> _logger;

    public GetAllConfigurationsCommandHandler(ConfigurationStorage configurationStorage, ILogger<GetAllConfigurationsCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<GetAllConfigurationsResponse> Handle(GetAllConfigurationsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение всех строк конфигурации");

        var configurations = await _configurationStorage.GetAllConfigurationsAsync();

        _logger.LogInformation("Все строки конфигурации получены. Параметров: {ConfigCount}", configurations.Count);

        return new GetAllConfigurationsResponse()
        {
            Configurations = { configurations.Select(c => new ConfigurationItem()
            {
                Key = c.Key,
                Value = c.Value,
                ServiceId = (int) c.ServiceId,
                EditedAt = Timestamp.FromDateTime(c.EditedAt),
                EditedBy = c.EditedBy,
                EditedFrom = c.EditedFrom,
                Section = c.Section
            }) }
        };
    }
}
