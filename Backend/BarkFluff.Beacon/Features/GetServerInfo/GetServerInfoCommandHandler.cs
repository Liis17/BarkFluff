using BarkFluff.Beacon.Configurations;
using BarkFluff.Proto.Beacon;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using MediatR;

namespace BarkFluff.Beacon.Features.GetServerInfo;

public class GetServerInfoCommandHandler : IRequestHandler<GetServerInfoCommand, GetServerInfoResponse>
{
    private readonly ServerColorSettings _serverColorSettings;
    private readonly ServerPropsSettings _serverPropsSettings;

    private readonly ConfigurationApi.ConfigurationApiClient _configurationApiClient;
    private readonly ILogger<GetServerInfoCommandHandler> _logger;

    public GetServerInfoCommandHandler(ServerColorSettings serverColorSettings, ServerPropsSettings serverPropsSettings,
        ConfigurationApi.ConfigurationApiClient configurationApiClient, ILogger<GetServerInfoCommandHandler> logger)
    {
        _serverColorSettings = serverColorSettings;
        _serverPropsSettings = serverPropsSettings;
        _configurationApiClient = configurationApiClient;
        _logger = logger;
    }

    public async Task<GetServerInfoResponse> Handle(GetServerInfoCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Запрос информации о сервере '{ServerName}'", _serverPropsSettings.Name);

        _logger.LogDebug("Получение конфигураций для всех микросервисов");

        var identitySettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Identity
        });

        var usersSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Users
        });

        var filesSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Files
        });

        var messagesSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Messages
        });

        var updatesSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Updates
        });

        var onlinerSettings = await _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest()
        {
            ServiceId = (int)ServiceId.Onliner
        });

        _logger.LogInformation(
            "Информация о сервере '{ServerName}' успешно собрана. Описание: {Description}",
            _serverPropsSettings.Name,
            _serverPropsSettings.Description
        );

        return new GetServerInfoResponse
        {
            Name = _serverPropsSettings.Name,
            Description = _serverPropsSettings.Description,

            Color = new ServerColor
            {
                HardHex = _serverColorSettings.Hard,
                LiteHex = _serverColorSettings.Lite,
                MainHex = _serverColorSettings.Main
            },

            Files = ParseService(ServiceId.Files, filesSettings.Configurations.ToList()),
            Identity = ParseService(ServiceId.Identity, identitySettings.Configurations.ToList()),
            Users = ParseService(ServiceId.Users, usersSettings.Configurations.ToList()),
            Messages = ParseService(ServiceId.Messages, messagesSettings.Configurations.ToList()),
            Updates = ParseService(ServiceId.Updates, updatesSettings.Configurations.ToList()),
            Onliner = ParseService(ServiceId.Onliner, onlinerSettings.Configurations.ToList()),
        };
    }

    private Service ParseService(ServiceId id, List<ConfigurationItem> settings)
    {
        // Внешний адрес сервиса (субдомен через nginx, порт 443)
        var externalHost = settings
            .FirstOrDefault(x => x.Section == "ExternalEndpoint" && x.Key == "Host")?.Value;

        // Если ExternalEndpoint не задан, фолбэк на RunSettings (обратная совместимость)
        if (string.IsNullOrWhiteSpace(externalHost))
        {
            externalHost = settings
                .FirstOrDefault(x => x.Section == "RunSettings" && x.Key == "Host")?.Value
                ?? $"https://{id.ToString().ToLower()}.example.com";
        }

        return new Service
        {
            Name = id.ToString(),
            Endpoint = new ServiceEndpoint
            {
                Host = externalHost,
                Port = 443
            },
            Status = ServiceStatus.Healthy,
            TlsEnabled = true
        };
    }
}