using BarkFluff.Beacon.Configurations;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Beacon;
using BarkFluff.Proto.Configuration;
using BarkFluff.Shared.Identity;

using MediatR;

using Microsoft.Extensions.Caching.Memory;

namespace BarkFluff.Beacon.Features.GetServerInfo;

public class GetServerInfoCommandHandler : IRequestHandler<GetServerInfoCommand, GetServerInfoResponse>
{
    private const string CacheKey = "GetServerInfoResponse";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ServerColorSettings _serverColorSettings;
    private readonly ServerPropsSettings _serverPropsSettings;

    private readonly ConfigurationApi.ConfigurationApiClient _configurationApiClient;
    private readonly ILogger<GetServerInfoCommandHandler> _logger;
    private readonly MetricsCollector _metrics;
    private readonly IMemoryCache _cache;

    public GetServerInfoCommandHandler(ServerColorSettings serverColorSettings, ServerPropsSettings serverPropsSettings,
        ConfigurationApi.ConfigurationApiClient configurationApiClient, ILogger<GetServerInfoCommandHandler> logger,
        MetricsCollector metrics, IMemoryCache cache)
    {
        _serverColorSettings = serverColorSettings;
        _serverPropsSettings = serverPropsSettings;
        _configurationApiClient = configurationApiClient;
        _logger = logger;
        _metrics = metrics;
        _cache = cache;
    }

    public async Task<GetServerInfoResponse> Handle(GetServerInfoCommand request, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out GetServerInfoResponse? cached) && cached is not null)
            return cached;

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
        var callsTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Calls }, cancellationToken: cancellationToken).ResponseAsync;
        var botsTask = _configurationApiClient
            .GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Bots }, cancellationToken: cancellationToken).ResponseAsync;

        try
        {
            await Task.WhenAll(identityTask, usersTask, filesTask, messagesTask, updatesTask, onlinerTask, fastAuthTask, callsTask, botsTask);
            _metrics.Add("configuration_fetch_success", 9);
        }
        catch
        {
            // Считаем сколько задач отвалилось, остальные считаем успешными.
            var failed = new[] { identityTask, usersTask, filesTask, messagesTask, updatesTask, onlinerTask, fastAuthTask, callsTask, botsTask }
                .Count(t => t.IsFaulted);
            _metrics.Add("configuration_fetch_errors", failed);
            _metrics.Add("configuration_fetch_success", 9 - failed);
            throw;
        }

        var identitySettings = identityTask.Result;
        var usersSettings = usersTask.Result;
        var filesSettings = filesTask.Result;
        var messagesSettings = messagesTask.Result;
        var updatesSettings = updatesTask.Result;
        var onlinerSettings = onlinerTask.Result;
        var fastAuthSettings = fastAuthTask.Result;
        var callsSettings = callsTask.Result;
        var botsSettings = botsTask.Result;

        _logger.LogInformation(
            "Информация о сервере '{ServerName}' успешно собрана. Описание: {Description}",
            _serverPropsSettings.Name,
            _serverPropsSettings.Description
        );

        var response = new GetServerInfoResponse
        {
            Name = _serverPropsSettings.Name,
            Description = _serverPropsSettings.Description,
            PublicName = _serverPropsSettings.PublicName,
            Location = _serverPropsSettings.Location,

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
            Calls = ParseService(ServiceId.Calls, callsSettings.Configurations),
            Bots = ParseService(ServiceId.Bots, botsSettings.Configurations),

            // WSS-адрес LiveKit для звонков (пусто, если Calls/LiveKit не настроены).
            LivekitUrl = callsSettings.Configurations
                .FirstOrDefault(x => x.Section == "LiveKit" && x.Key == "Url")?.Value ?? string.Empty,
        };

        _cache.Set(CacheKey, response, CacheTtl);
        return response;
    }

    private Service ParseService(ServiceId id, IEnumerable<ConfigurationItem> settings)
    {
        // Внешний адрес сервиса (субдомен через nginx, порт 443)
        var externalHost = settings
            .FirstOrDefault(x => x.Section == "ExternalEndpoint" && x.Key == "Host")?.Value;

        if (string.IsNullOrWhiteSpace(externalHost))
        {
            _logger.LogError("Для сервиса {ServiceId} не задан ExternalEndpoint:Host", id);
            return new Service
            {
                Name = id.ToString(),
                Endpoint = new ServiceEndpoint { Host = string.Empty, Port = 0 },
                Status = ServiceStatus.Offline,
                TlsEnabled = false
            };
        }

        return new Service
        {
            Name = id.ToString(),
            Endpoint = new ServiceEndpoint
            {
                Host = NormalizeHost(externalHost),
                Port = 443
            },
            Status = ServiceStatus.Healthy,
            TlsEnabled = true
        };
    }

    private static string NormalizeHost(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }

        return value.Trim().TrimEnd('/');
    }
}