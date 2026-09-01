using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Navigator;

namespace BarkFluff.Beacon.BackgroundServices;

using Configurations;

public class ServerRegistrationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerRegistrationService> _logger;
    private readonly MetricsCollector _metrics;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public ServerRegistrationService(IServiceProvider serviceProvider,
        ILogger<ServerRegistrationService> logger, MetricsCollector metrics)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var navigatorClient = scope.ServiceProvider.GetRequiredService<NavigatorApi.NavigatorApiClient>();
                var configurationApiClient = scope.ServiceProvider
                    .GetRequiredService<BarkFluff.Proto.Configuration.ConfigurationApi.ConfigurationApiClient>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var serverProps = scope.ServiceProvider.GetRequiredService<ServerPropsSettings>();
                var colorSettings = scope.ServiceProvider.GetRequiredService<ServerColorSettings>();

                // Преобразуем в ServerInfo для RegisterServerRequest
                // Используем внешний адрес Beacon (субдомен через nginx, порт 443)
                var externalHost = config["ExternalEndpoint:Host"];
                if (string.IsNullOrWhiteSpace(externalHost))
                {
                    _logger.LogError("Не задан ExternalEndpoint:Host — регистрация в Navigator пропущена");
                    await Task.Delay(_interval, stoppingToken);
                    continue;
                }

                // Navigator валидирует BeaconHost как hostname/IP без схемы — нормализуем,
                // если в конфиге лежит полный URL вида "https://beacon.example.com".
                externalHost = NormalizeHost(externalHost);

                // gRPC-Web шлюз ноды — то, к чему браузер подключается напрямую с глобального
                // web.barkfluff.com. Берём внешний адрес BarkFluff.Web из Settings; если
                // он не задан, нода просто не попадёт в список выбора веб-клиента.
                var webEndpoint = await GetWebEndpointAsync(configurationApiClient, stoppingToken);

                // Отдельный адрес файлового HTTP в обход CDN. Пустой — нода отдаёт файлы
                // только по основному адресу Files, как было до появления этого поля.
                var filesMediaEndpoint = await GetFilesMediaEndpointAsync(configurationApiClient, stoppingToken);

                var serverInfo = new ServerInfo
                {
                    WebEndpoint = webEndpoint,
                    FilesMediaEndpoint = filesMediaEndpoint,
                    Name = serverProps.Name,
                    Description = serverProps.Description,
                    ServerPublicName = serverProps.PublicName ?? string.Empty,
                    Location = serverProps.Location ?? string.Empty,
                    AccountsCount = 0, // Можно доработать, если нужно
                    BeaconUri = new ServiceEndpoint
                    {
                        Host = externalHost,
                        Port = 443
                    },
                    Color = new ServerColor
                    {
                        LiteHex = colorSettings.Lite ?? string.Empty,
                        MainHex = colorSettings.Main ?? string.Empty,
                        HardHex = colorSettings.Hard ?? string.Empty
                    }
                };

                var request = new RegisterServerRequest { Server = serverInfo };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await navigatorClient.RegisterServerAsync(request, cancellationToken: stoppingToken);
                _metrics.Increment("navigator_registrations");
                _metrics.Add("navigator_registration_duration_ms_total", sw.ElapsedMilliseconds);
                _metrics.Set("last_navigator_registration_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _metrics.Set("navigator_registration_healthy", 1);
                _logger.LogInformation("RegisterServer успешно отправлен в Navigator");
            }
            catch (Exception ex)
            {
                _metrics.Increment("navigator_registration_errors");
                _metrics.Set("navigator_registration_healthy", 0);
                _logger.LogError(ex, "Ошибка при отправке RegisterServer");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task<string> GetWebEndpointAsync(
        BarkFluff.Proto.Configuration.ConfigurationApi.ConfigurationApiClient client,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetConfigurationAsync(
                new BarkFluff.Proto.Configuration.GetConfigurationRequest { ServiceId = (int)BarkFluff.Shared.Identity.ServiceId.Web },
                cancellationToken: ct);

            var host = response.Configurations
                .FirstOrDefault(c => c.Section == "ExternalEndpoint" && c.Key == "Host")?.Value;

            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogInformation("ExternalEndpoint:Host для Web не задан — нода не будет предлагаться веб-клиенту");
                return string.Empty;
            }

            // Navigator ждёт абсолютный origin; в конфиге может лежать как голый хост, так и полный URL.
            return host.Contains("://", StringComparison.Ordinal)
                ? host.TrimEnd('/')
                : $"https://{NormalizeHost(host)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить ExternalEndpoint:Host для Web — регистрируемся без web_endpoint");
            return string.Empty;
        }
    }

    private async Task<string> GetFilesMediaEndpointAsync(
        BarkFluff.Proto.Configuration.ConfigurationApi.ConfigurationApiClient client,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetConfigurationAsync(
                new BarkFluff.Proto.Configuration.GetConfigurationRequest { ServiceId = (int)BarkFluff.Shared.Identity.ServiceId.Files },
                cancellationToken: ct);

            var host = response.Configurations
                .FirstOrDefault(c => c.Section == "ExternalEndpoint" && c.Key == "MediaHost")?.Value;

            if (string.IsNullOrWhiteSpace(host))
                return string.Empty;

            // Navigator ждёт абсолютный origin; в конфиге может лежать как голый хост, так и полный URL.
            return host.Contains("://", StringComparison.Ordinal)
                ? host.TrimEnd('/')
                : $"https://{NormalizeHost(host)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить ExternalEndpoint:MediaHost для Files — регистрируемся без files_media_endpoint");
            return string.Empty;
        }
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
