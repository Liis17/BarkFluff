using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.Beacon;
using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public static class NodeConnectionMapper
{
    public static NodeConnection Map(string beaconAddress, GetServerInfoResponse serverInfo)
    {
        ArgumentNullException.ThrowIfNull(serverInfo);

        var parameters = new GlobalParam
        {
            SocketBeacon = beaconAddress,
            ServerName = serverInfo.Name,
            ServerDescription = serverInfo.Description,
            SocketIdentity = GetEndpointUrl(serverInfo.Identity),
            SocketUsers = GetEndpointUrl(serverInfo.Users),
            SocketFiles = GetEndpointUrl(serverInfo.Files),
            SocketFilesMedia = serverInfo.FilesMediaEndpoint,
            SocketMessages = GetEndpointUrl(serverInfo.Messages),
            SocketUpdates = GetEndpointUrl(serverInfo.Updates),
            SocketOnliner = GetEndpointUrl(serverInfo.Onliner),
            SocketFastAuth = GetEndpointUrl(serverInfo.FastAuth),
            SocketCalls = GetOptionalEndpointUrl(serverInfo.Calls),
            LivekitUrl = serverInfo.LivekitUrl,
            ServerDnsName = serverInfo.ServerName,
            FederationEnabled = serverInfo.FederationEnabled,
            Colors = new ClientColors
            {
                LiteHex = serverInfo.Color?.LiteHex ?? string.Empty,
                MainHex = serverInfo.Color?.MainHex ?? string.Empty,
                HardHex = serverInfo.Color?.HardHex ?? string.Empty
            }
        };

        return new NodeConnection(
            new NodeProfile(beaconAddress, serverInfo.Name, serverInfo.Description),
            parameters);
    }

    private static string GetEndpointUrl(Service? service)
    {
        if (service?.Endpoint is null
            || string.IsNullOrWhiteSpace(service.Endpoint.Host)
            || service.Endpoint.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Node response does not contain a required service endpoint.");
        }

        return WebApiClient.BuildEndpointUrl(service.Endpoint.Host, service.Endpoint.Port, service.TlsEnabled);
    }

    private static string GetOptionalEndpointUrl(Service? service)
    {
        return service?.Endpoint is { Host.Length: > 0, Port: > 0 and <= 65535 }
            ? WebApiClient.BuildEndpointUrl(service.Endpoint.Host, service.Endpoint.Port, service.TlsEnabled)
            : string.Empty;
    }
}
