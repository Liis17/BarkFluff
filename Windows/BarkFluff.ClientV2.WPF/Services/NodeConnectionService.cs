using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.ClientV2.WPF.Services;

public sealed class NodeConnectionService : INodeConnectionService
{
    private const string ConnectionFailedResourceKey = "Error_NodeConnectionFailed";

    private readonly WebApiClient _webApi;
    private readonly INodeAddressParser _addressParser;
    private readonly IClientSession _session;

    public NodeConnectionService(WebApiClient webApi, INodeAddressParser addressParser, IClientSession session)
    {
        _webApi = webApi;
        _addressParser = addressParser;
        _session = session;
    }

    public async Task<IReadOnlyList<PublicNode>> GetPublicNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_webApi.CreateNavigatorAC().IsSuccess)
        {
            return [];
        }

        var response = await _webApi.GetServerList(new GlobalParam());
        if (!response.Item1.IsSuccess)
        {
            return [];
        }

        return response.ServerElements
            .Select(node => new PublicNode(node.Ip, node.Title, node.Description, node.Location, node.UserCount))
            .ToList();
    }

    public async Task<NodeConnectionResult> ConnectAsync(string address, CancellationToken cancellationToken = default)
    {
        var parsedAddress = _addressParser.Parse(address);
        if (!parsedAddress.IsSuccess)
        {
            return NodeConnectionResult.Failure(GetErrorResourceKey(parsedAddress.Error));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new GlobalParam { SocketBeacon = parsedAddress.Address!.GetLeftPart(UriPartial.Authority) };
        if (!_webApi.CreateOnlyBeaconAC(parameters).IsSuccess)
        {
            return NodeConnectionResult.Failure(ConnectionFailedResourceKey);
        }

        var response = await _webApi.GetServerInfo(parameters);
        if (!response.error.IsSuccess || response.Item2 is null)
        {
            return NodeConnectionResult.Failure(ConnectionFailedResourceKey);
        }

        try
        {
            var connection = NodeConnectionMapper.Map(parameters.SocketBeacon, response.Item2);
            return RestoreConnection(connection)
                ? NodeConnectionResult.Success(connection)
                : NodeConnectionResult.Failure(ConnectionFailedResourceKey);
        }
        catch (InvalidOperationException)
        {
            return NodeConnectionResult.Failure(ConnectionFailedResourceKey);
        }
    }

    public bool RestoreConnection(NodeConnection connection)
    {
        var initialized = _webApi.CreateAC(
            connection.ConnectionParameters,
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            "BarkFluff",
            "2.0",
            string.Empty);
        if (!initialized.IsSuccess)
        {
            return false;
        }

        _session.SetConnection(connection);
        return true;
    }

    private static string GetErrorResourceKey(NodeAddressError error) => error switch
    {
        NodeAddressError.Required => "Error_NodeAddressRequired",
        NodeAddressError.IpPortRequired => "Error_NodeIpPortRequired",
        _ => "Error_NodeAddressInvalid"
    };
}
