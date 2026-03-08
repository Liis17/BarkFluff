namespace BarkFluff.Navigator.Features.ListServers;

using MediatR;

using Microsoft.Extensions.Logging;

using Persistence;

using Proto.Navigator;

public class ListServersQueryHandler : IRequestHandler<ListServersQuery, ListServersResponse>
{

    private readonly ServersStorage _serversStorage;
    private readonly ILogger<ListServersQueryHandler> _logger;

    public ListServersQueryHandler(ServersStorage serversStorage, ILogger<ListServersQueryHandler> logger)
    {
        _serversStorage = serversStorage;
        _logger = logger;
    }

    public async Task<ListServersResponse> Handle(ListServersQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос списка зарегистрированных серверов");

        var servers = await _serversStorage.GetServers();

        _logger.LogInformation(
            "Получен список серверов. Количество: {ServerCount}",
            servers.Count()
        );

        return new ListServersResponse()
        {
            Servers =
            {
                servers.Select(server =>
                    new ServerInfo
                    {
                        Name = server.Name,
                        Description = server.Description,
                        ServerPublicName = server.ServerPublicName,
                        Location = server.Location,
                        AccountsCount = 0,
                        BeaconUri = new ServiceEndpoint()
                        {
                            Host = server.BeaconHost, Port = server.BeaconPort
                        },
                        Color = new ServerColor
                        {
                            LiteHex = server.ColorLiteHex,
                            MainHex = server.ColorMainHex,
                            HardHex = server.ColorHardHex
                        }
                    })
            }
        };
    }
}