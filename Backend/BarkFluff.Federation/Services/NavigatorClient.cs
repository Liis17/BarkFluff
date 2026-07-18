using BarkFluff.Proto.Navigator;

using Grpc.Core;

namespace BarkFluff.Federation.Services;

// Источник 2 discovery (docs/rearch/03-discovery.md) — вторичный канал, каталог публичных нод.
// Navigator без авторизации (как у Beacon).
public class NavigatorClient : INavigatorClient
{
    private readonly NavigatorApi.NavigatorApiClient _client;
    private readonly ILogger<NavigatorClient> _logger;

    public NavigatorClient(NavigatorApi.NavigatorApiClient client, ILogger<NavigatorClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<RemoteServerDocument?> GetServerByNameAsync(string serverName, CancellationToken ct = default)
    {
        GetServerByNameResponse response;
        try
        {
            response = await _client.GetServerByNameAsync(new GetServerByNameRequest { ServerName = serverName }, cancellationToken: ct);
        }
        catch (RpcException ex)
        {
            _logger.LogInformation("Navigator.GetServerByName({Server}) недоступен: {Status}", serverName, ex.Status);
            return null;
        }

        if (!response.Found)
            return null;

        var server = response.Server;
        var keys = server.SigningKeys
            .Select(k => new RemoteSigningKey(
                k.KeyId,
                k.PublicKey.ToByteArray(),
                k.ExpiredAt != null ? k.ExpiredAt.ToDateTime() : null))
            .ToList();

        return new RemoteServerDocument(
            server.ServerName,
            server.FederationEndpoint,
            server.TlsSpkiSha256.ToArray(),
            server.FederationProtocolVersions.ToArray(),
            keys);
    }
}
