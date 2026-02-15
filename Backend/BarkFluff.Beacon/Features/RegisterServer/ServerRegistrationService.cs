using System;
using System.Threading;
using System.Threading.Tasks;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Navigator;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Beacon.Features.RegisterServer;

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
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var serverProps = scope.ServiceProvider.GetRequiredService<ServerPropsSettings>();
                var colorSettings = scope.ServiceProvider.GetRequiredService<ServerColorSettings>();

                // Преобразуем в ServerInfo для RegisterServerRequest
                // Используем внешний адрес Beacon (субдомен через nginx, порт 443)
                var externalHost = config["ExternalEndpoint:Host"];
                if (string.IsNullOrWhiteSpace(externalHost))
                {
                    // Фолбэк на RunSettings для обратной совместимости
                    externalHost = config["RunSettings:Host"] ?? "https://beacon.example.com";
                }

                var serverInfo = new ServerInfo
                {
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
                await navigatorClient.RegisterServerAsync(request, cancellationToken: stoppingToken);
                _metrics.Increment("navigator_registrations");
                _logger.LogInformation("RegisterServer успешно отправлен в Navigator");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке RegisterServer");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
} 