using BarkFluff.Navigator.Domain;
using BarkFluff.Navigator.Features.RegisterServer;
using BarkFluff.Navigator.Persistence;
using BarkFluff.Shared.Exceptions.Federation;
using BarkFluff.Shared.Exceptions.Navigator;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Navigator.Tests.Features;

file class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

// Тестовый двойник: не ходит в сеть, всегда возвращает заданный результат "well-known подтвердил владение".
file class StubWellKnownValidator(bool result) : FederationWellKnownValidator(new FakeHostEnvironment(), NullLogger<FederationWellKnownValidator>.Instance)
{
    public override Task<bool> ValidateAsync(string normalizedServerName, IReadOnlyList<(string KeyId, byte[] PublicKey)> claimedKeys, CancellationToken ct)
        => Task.FromResult(result);
}

public class RegisterServerCommandHandlerTests
{
    private static NavigatorContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NavigatorContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NavigatorContext(options);
    }

    private readonly NavigatorContext _context = CreateContext();
    private readonly ServersStorage _storage;

    public RegisterServerCommandHandlerTests()
    {
        var configuration = new ConfigurationBuilder().Build();
        _storage = new ServersStorage(_context, new RegistrationThrottle(configuration), configuration);
    }

    private RegisterServerCommandHandler CreateHandler(bool wellKnownResult = true) =>
        new(_storage, new StubWellKnownValidator(wellKnownResult), NullLogger<RegisterServerCommandHandler>.Instance);

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

    private Task<BarkFluff.Proto.Navigator.RegisterServerResponse> Invoke(ServerInfo server, bool wellKnownResult = true) =>
        CreateHandler(wellKnownResult).Handle(new RegisterServerCommand { Server = server }, CancellationToken.None);

    [Fact]
    public async Task Handle_ValidServer_RegistersAndIsListed()
    {
        var result = await Invoke(ValidServer());

        result.Should().NotBeNull();
        (await _storage.GetServersAsync()).Should().ContainSingle(s => s.Name == "MyServer");
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

    [Fact]
    public async Task Handle_ServerNameSet_WellKnownConfirms_RegistersWithFederationFields()
    {
        var server = ValidServer();
        server.ServerName = "Chat.Example.ORG"; // проверяем lowercase-нормализацию

        await Invoke(server, wellKnownResult: true);

        var stored = await _storage.GetByServerNameAsync("chat.example.org");
        stored.Should().NotBeNull();
        stored!.ServerName.Should().Be("chat.example.org");
    }

    [Fact]
    public async Task Handle_ServerNameSet_WellKnownRejects_ThrowsFederationRegistrationRejected()
    {
        var server = ValidServer();
        server.ServerName = "chat.example.org";

        await FluentActions.Awaiting(() => Invoke(server, wellKnownResult: false))
            .Should().ThrowAsync<FederationRegistrationRejectedException>();
    }

    [Fact]
    public async Task Handle_ServerNameIsIpLiteral_ThrowsInvalidServername()
    {
        var server = ValidServer();
        server.ServerName = "10.0.0.1";

        await FluentActions.Awaiting(() => Invoke(server))
            .Should().ThrowAsync<InvalidServernameException>();
    }
}
