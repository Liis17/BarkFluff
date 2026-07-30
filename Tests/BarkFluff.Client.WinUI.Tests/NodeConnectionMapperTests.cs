using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Beacon;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class NodeConnectionMapperTests
{
    [Fact]
    public void Map_CompleteBeaconResponse_MapsAllRequiredEndpoints()
    {
        var response = new GetServerInfoResponse
        {
            Name = "Example node",
            Description = "Test node",
            Identity = CreateService("identity.example.com", 443, true),
            Users = CreateService("users.example.com", 7001, false),
            Files = CreateService("files.example.com", 443, true),
            Messages = CreateService("messages.example.com", 443, true),
            Updates = CreateService("updates.example.com", 443, true),
            Onliner = CreateService("onliner.example.com", 443, true),
            FastAuth = CreateService("fastauth.example.com", 443, true),
            Calls = CreateService("calls.example.com", 443, true),
            ServerName = "example.com",
            FederationEnabled = true
        };

        var connection = NodeConnectionMapper.Map("https://beacon.example.com", response);

        Assert.Equal("Example node", connection.Profile.Name);
        Assert.Equal("https://identity.example.com:443", connection.ConnectionParameters.SocketIdentity);
        Assert.Equal("http://users.example.com:7001", connection.ConnectionParameters.SocketUsers);
        Assert.Equal("https://calls.example.com:443", connection.ConnectionParameters.SocketCalls);
        Assert.True(connection.ConnectionParameters.FederationEnabled);
    }

    [Fact]
    public void Map_MissingRequiredEndpoint_Throws()
    {
        var response = new GetServerInfoResponse();

        Assert.Throws<InvalidOperationException>(() => NodeConnectionMapper.Map("https://node.example.com", response));
    }

    private static Service CreateService(string host, int port, bool tlsEnabled) => new()
    {
        Endpoint = new ServiceEndpoint { Host = host, Port = port },
        TlsEnabled = tlsEnabled
    };
}
