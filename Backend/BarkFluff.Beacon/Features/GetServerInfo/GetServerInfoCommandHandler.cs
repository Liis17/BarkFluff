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

        var identityTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Identity }, cancellationToken: cancellationToken).ResponseAsync;
        var usersTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Users }, cancellationToken: cancellationToken).ResponseAsync;
        var filesTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Files }, cancellationToken: cancellationToken).ResponseAsync;
        var messagesTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Messages }, cancellationToken: cancellationToken).ResponseAsync;
        var updatesTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Updates }, cancellationToken: cancellationToken).ResponseAsync;
        var onlinerTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Onliner }, cancellationToken: cancellationToken).ResponseAsync;
        var fastAuthTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.FastAuth }, cancellationToken: cancellationToken).ResponseAsync;

        await Task.WhenAll(identityTask, usersTask, filesTask, messagesTask, updatesTask, onlinerTask, fastAuthTask);

        var identitySettings = identityTask.Result;
        var usersSettings = usersTask.Result;
        var filesSettings = filesTask.Result;
        var messagesSettings = messagesTask.Result;
        var updatesSettings = updatesTask.Result;
        var onlinerSettings = onlinerTask.Result;
        var fastAuthSettings = fastAuthTask.Result;

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

            Files = ParseService(ServiceId.Files, filesSettings.Configurations),
            Identity = ParseService(ServiceId.Identity, identitySettings.Configurations),
            Users = ParseService(ServiceId.Users, usersSettings.Configurations),
            Messages = ParseService(ServiceId.Messages, messagesSettings.Configurations),
            Updates = ParseService(ServiceId.Updates, updatesSettings.Configurations),
            Onliner = ParseService(ServiceId.Onliner, onlinerSettings.Configurations),
            FastAuth = ParseService(ServiceId.FastAuth, fastAuthSettings.Configurations),
        };
    }

    private Service ParseService(ServiceId id, IEnumerable<ConfigurationItem> settings)
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