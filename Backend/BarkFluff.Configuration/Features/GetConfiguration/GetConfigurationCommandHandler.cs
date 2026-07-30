using BarkFluff.Configuration.Persistence;
using BarkFluff.Proto.Configuration;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Configuration.Features.GetConfiguration;

public class GetConfigurationCommandHandler : IRequestHandler<GetConfigurationCommand, GetConfigurationResponse>
{
    private readonly ConfigurationStorage _configurationStorage;
    private readonly ILogger<GetConfigurationCommandHandler> _logger;

    public GetConfigurationCommandHandler(ConfigurationStorage configurationStorage, ILogger<GetConfigurationCommandHandler> logger)
    {
        _configurationStorage = configurationStorage;
        _logger = logger;
    }

    public async Task<GetConfigurationResponse> Handle(GetConfigurationCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение конфигурации для сервиса {ServiceId}", request.ServiceId);

        var configurations = await _configurationStorage.GetConfiguration(request.ServiceId);

        var filteredConfigurations = configurations
            .GroupBy(c => new { c.Section, c.Key })
            .Select(group => group
                .OrderByDescending(c => c.ServiceId == request.ServiceId)
                .First())
            .ToList();

        _logger.LogInformation(
            "Конфигурация для сервиса {ServiceId} получена. Параметров: {ConfigCount}",
            request.ServiceId,
            filteredConfigurations.Count
        );

        return new GetConfigurationResponse()
        {
            Configurations = { filteredConfigurations.Select(c => new ConfigurationItem()
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