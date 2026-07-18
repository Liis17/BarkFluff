using BarkFluff.Navigator.Domain;
using BarkFluff.Navigator.Features.GetServerByName;
using BarkFluff.Navigator.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Navigator.Tests.Features;

public class GetServerByNameQueryHandlerTests
{
    private static (NavigatorContext Context, ServersStorage Storage) CreateStorage()
    {
        var options = new DbContextOptionsBuilder<NavigatorContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new NavigatorContext(options);
        var configuration = new ConfigurationBuilder().Build();
        var storage = new ServersStorage(context, new RegistrationThrottle(configuration), configuration);
        return (context, storage);
    }

    [Fact]
    public async Task Handle_UnknownServer_ReturnsFoundFalse()
    {
        var (_, storage) = CreateStorage();
        var handler = new GetServerByNameQueryHandler(storage, NullLogger<GetServerByNameQueryHandler>.Instance);

        var response = await handler.Handle(new GetServerByNameQuery { ServerName = "unknown.example.org" }, CancellationToken.None);

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_KnownServer_ReturnsFoundTrueWithFederationFields()
    {
        var (context, storage) = CreateStorage();
        context.Servers.Add(new ServerInfo
        {
            BeaconHost = "beacon.example.org",
            BeaconPort = 443,
            Name = "srv",
            Description = "desc",
            AddedBy = "admin",
            ServerName = "chat.example.org",
            FederationEndpoint = "https://fed.chat.example.org",
            TlsSpkiSha256 = ["abc123=="],
            FederationProtocolVersions = [1],
            SigningKeys = [new NavigatorSigningKeyInfo { KeyId = "ed25519:1", PublicKeyBase64 = Convert.ToBase64String(new byte[32]) }],
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var handler = new GetServerByNameQueryHandler(storage, NullLogger<GetServerByNameQueryHandler>.Instance);
        var response = await handler.Handle(new GetServerByNameQuery { ServerName = "CHAT.example.org" }, CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Server.ServerName.Should().Be("chat.example.org");
        response.Server.FederationEndpoint.Should().Be("https://fed.chat.example.org");
        response.Server.SigningKeys.Should().ContainSingle(k => k.KeyId == "ed25519:1");
    }

    [Fact]
    public async Task Handle_StaleServer_ReturnsFoundFalse()
    {
        var (context, storage) = CreateStorage();
        context.Servers.Add(new ServerInfo
        {
            BeaconHost = "beacon.example.org",
            BeaconPort = 443,
            Name = "srv",
            Description = "desc",
            AddedBy = "admin",
            ServerName = "stale.example.org",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow.AddHours(-1), // старше ActivePeriodMinutes (дефолт 10 мин)
        });
        await context.SaveChangesAsync();

        var handler = new GetServerByNameQueryHandler(storage, NullLogger<GetServerByNameQueryHandler>.Instance);
        var response = await handler.Handle(new GetServerByNameQuery { ServerName = "stale.example.org" }, CancellationToken.None);

        response.Found.Should().BeFalse();
    }
}
