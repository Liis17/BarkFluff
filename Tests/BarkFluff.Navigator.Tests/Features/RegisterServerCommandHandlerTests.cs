using BarkFluff.Navigator.Domain;
using BarkFluff.Navigator.Features.RegisterServer;
using BarkFluff.Navigator.Persistence;
using BarkFluff.Shared.Exceptions.Navigator;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Navigator.Tests.Features;

public class RegisterServerCommandHandlerTests
{
    private readonly ServersStorage _storage =
        new(new ConfigurationBuilder().Build());

    private RegisterServerCommandHandler CreateHandler() =>
        new(_storage, NullLogger<RegisterServerCommandHandler>.Instance);

    private static ServerInfo ValidServer() => new()
    {
        BeaconHost = "beacon.example.com",
        BeaconPort = 443,
        Name = "MyServer",
        Description = "A test server",
        ServerPublicName = "Public Name",
        Location = "EU",
        ColorLiteHex = "#AABBCC",
        ColorMainHex = "112233",
        ColorHardHex = "",
        AddedBy = "admin",
    };

    private Task<BarkFluff.Proto.Navigator.RegisterServerResponse> Invoke(ServerInfo server) =>
        CreateHandler().Handle(new RegisterServerCommand { Server = server }, CancellationToken.None);

    [Fact]
    public async Task Handle_ValidServer_RegistersAndIsListed()
    {
        var result = await Invoke(ValidServer());

        result.Should().NotBeNull();
        _storage.GetServers().Should().ContainSingle(s => s.Name == "MyServer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_EmptyBeaconHost_ThrowsBeaconHostEmpty(string host)
    {
        var server = ValidServer();
        server.BeaconHost = host;

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<BeaconHostEmptyException>();
    }

    [Theory]
    [InlineData("not a valid host!!")]
    [InlineData("http://")]
    public async Task Handle_InvalidBeaconHost_ThrowsInvalidBeaconHost(string host)
    {
        var server = ValidServer();
        server.BeaconHost = host;

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<InvalidBeaconHostException>();
    }

    [Theory]
    [InlineData("beacon.example.com")]
    [InlineData("https://beacon.example.com")]
    [InlineData("192.168.0.1")]
    public async Task Handle_ValidBeaconHostForms_Succeed(string host)
    {
        var server = ValidServer();
        server.BeaconHost = host;

        await FluentActions.Awaiting(() => Invoke(server)).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(70000)]
    public async Task Handle_PortOutOfRange_ThrowsArgumentException(int port)
    {
        var server = ValidServer();
        server.BeaconPort = port;

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsNameEmpty()
    {
        var server = ValidServer();
        server.Name = "  ";

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<NameEmptyException>();
    }

    [Fact]
    public async Task Handle_NameTooLong_ThrowsArgumentException()
    {
        var server = ValidServer();
        server.Name = new string('x', 65);

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_EmptyDescription_ThrowsArgumentException()
    {
        var server = ValidServer();
        server.Description = "";

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_EmptyPublicName_ThrowsArgumentException()
    {
        var server = ValidServer();
        server.ServerPublicName = "";

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_LocationTooLong_ThrowsArgumentException()
    {
        var server = ValidServer();
        server.Location = new string('x', 129);

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("xyz123")]
    [InlineData("#FFF")]
    [InlineData("GGGGGG")]
    [InlineData("#AABBC")]
    public async Task Handle_InvalidHexColor_ThrowsInvalidHexColor(string color)
    {
        var server = ValidServer();
        server.ColorMainHex = color;

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<InvalidHexColorException>();
    }

    [Theory]
    [InlineData("AABBCC")]
    [InlineData("#AABBCC")]
    [InlineData("")]
    public async Task Handle_ValidOrEmptyHexColor_Succeeds(string color)
    {
        var server = ValidServer();
        server.ColorMainHex = color;

        await FluentActions.Awaiting(() => Invoke(server)).Should().NotThrowAsync();
    }
}
