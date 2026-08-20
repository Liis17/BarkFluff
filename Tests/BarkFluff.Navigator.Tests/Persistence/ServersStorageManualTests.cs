using BarkFluff.Navigator.Domain;
using BarkFluff.Navigator.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Navigator.Tests.Persistence;

public class ServersStorageManualTests
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

    private static ServerInfo ManualServer() => new()
    {
        BeaconHost = "beacon.example.org",
        BeaconPort = 443,
        Name = "manual",
        Description = "вручную добавленный сервер",
        ServerPublicName = "Manual Server",
        AddedBy = "admin",
    };

    [Fact]
    public async Task AddManualServer_AlwaysVisible_EvenAfterTtlExpires()
    {
        var (context, storage) = CreateStorage();

        await storage.AddManualServerAsync(ManualServer());

        // Имитируем протухание: ручная запись старше ActivePeriodMinutes (дефолт 10 мин).
        var row = await context.Servers.SingleAsync();
        row.LastSeenAt = DateTime.UtcNow.AddHours(-1);
        await context.SaveChangesAsync();

        var servers = await storage.GetServersAsync();
        servers.Should().ContainSingle(s => s.Name == "manual");
        servers[0].IsManual.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterServer_UpsertOverManualRow_PreservesIsManual()
    {
        var (context, storage) = CreateStorage();
        await storage.AddManualServerAsync(ManualServer());

        // Реальная нода регистрируется с тем же легаси-ключом Name+BeaconHost+BeaconPort.
        await storage.RegisterServerAsync(new ServerInfo
        {
            BeaconHost = "beacon.example.org",
            BeaconPort = 443,
            Name = "manual",
            Description = "обновлено регистрацией",
            ServerPublicName = "Manual Server",
            AddedBy = "node",
        });

        var servers = await storage.GetServersAsync();
        servers.Should().ContainSingle();
        servers[0].IsManual.Should().BeTrue();
        servers[0].Description.Should().Be("обновлено регистрацией");
    }

    [Fact]
    public async Task DeleteManualServer_DeletesOnlyManualRows()
    {
        var (context, storage) = CreateStorage();

        context.Servers.Add(new ServerInfo
        {
            BeaconHost = "beacon.example.org",
            BeaconPort = 443,
            Name = "auto",
            Description = "авто-регистрация",
            AddedBy = "node",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
        var autoId = (await context.Servers.SingleAsync(s => s.Name == "auto")).Id;

        await storage.AddManualServerAsync(ManualServer());
        var manualId = (await context.Servers.SingleAsync(s => s.Name == "manual")).Id;

        // Авто-регистрацию удалить нельзя.
        (await storage.DeleteManualServerAsync(autoId)).Should().BeFalse();
        (await context.Servers.CountAsync()).Should().Be(2);

        // Ручную — можно.
        (await storage.DeleteManualServerAsync(manualId)).Should().BeTrue();
        var remaining = await context.Servers.ToListAsync();
        remaining.Should().ContainSingle(s => s.Name == "auto");
    }
}
