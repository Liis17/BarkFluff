namespace BarkFluff.Navigator.Features.ListServers;

using MediatR;
using Persistence;
using Proto.Navigator;

public class ListServersQueryHandler : IRequestHandler<ListServersQuery, ListServersResponse>
{
    
    private readonly ServersStorage _serversStorage;

    public ListServersQueryHandler(ServersStorage serversStorage)
    {
        _serversStorage = serversStorage;
    }

    public async Task<ListServersResponse> Handle(ListServersQuery request, CancellationToken cancellationToken)
    {
        var servers = await _serversStorage.GetServers();

        return new ListServersResponse()
        {
            Servers =
            {
                servers.Select(server =>
                    new ServerInfo
                    {
                        Name = server.Name, Description = server.Description, AccountsCount = 0,
                        BeaconUri = new ServiceEndpoint()
                        {
                            Host = server.BeaconHost, Port = server.BeaconPort
                        }
                    })
            }
        };
    }
}