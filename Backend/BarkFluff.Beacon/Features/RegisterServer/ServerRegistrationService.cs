using System;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public ServerRegistrationService(IServiceProvider serviceProvider,
        ILogger<ServerRegistrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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

                // Преобразуем в ServerInfo для RegisterServerRequest
                var serverInfo = new ServerInfo
                {
                    Name = serverProps.Name,
                    Description = serverProps.Description,
                    ServerPublicName = serverProps.PublicName ?? string.Empty,
                    Location = serverProps.Location ?? string.Empty,
                    AccountsCount = 0, // Можно доработать, если нужно
                    BeaconUri = new ServiceEndpoint
                    {
                        Host = config["RunSettings:Host"],
                        Port = int.Parse(config["RunSettings:Port"])
                    }
                };

                var request = new RegisterServerRequest { Server = serverInfo };
                await navigatorClient.RegisterServerAsync(request, cancellationToken: stoppingToken);
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