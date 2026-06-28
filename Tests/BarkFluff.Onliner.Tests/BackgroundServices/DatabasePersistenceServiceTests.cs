using System.Reflection;
using BarkFluff.Onliner.BackgroundServices;
using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class DatabasePersistenceServiceTests
{
    private readonly TestHelper _h = new();

    private static readonly MethodInfo SaveMethod =
        typeof(DatabasePersistenceService).GetMethod("SaveStatusesToDatabaseAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public async Task SaveStatusesToDatabaseAsync_InsertsNewRecords()
    {
        _h.Storage.UpdateStatus(1);
        _h.Storage.UpdateStatus(2);
        using var sp = CreateServiceProvider();
        var service = CreateService(sp);

        await InvokeSaveAsync(service, CancellationToken.None);

        var ctx = sp.GetRequiredService<OnlineStatusContext>();
        var statuses = await ctx.UsersOnlineStatuses.ToListAsync();
        statuses.Should().HaveCount(2);
        statuses.Should().Contain(s => s.UserId == 1 && s.Status == DomainStatusTypeId.Online);
        statuses.Should().Contain(s => s.UserId == 2 && s.Status == DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task SaveStatusesToDatabaseAsync_UpdatesExistingRecords()
    {
        await _h.SeedDbStatus(1, DomainStatusTypeId.Online, DateTime.UtcNow.AddMinutes(-5));
        _h.Storage.UpdateStatus(1);
        _h.Storage.SetOffline(1);
        using var sp = CreateServiceProvider();
        var service = CreateService(sp);

        await InvokeSaveAsync(service, CancellationToken.None);

        var ctx = sp.GetRequiredService<OnlineStatusContext>();
        var status = await ctx.UsersOnlineStatuses.FindAsync(1L);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Offline);
    }

    [Fact]
    public async Task SaveStatusesToDatabaseAsync_MixedInsertAndUpdate()
    {
        await _h.SeedDbStatus(1, DomainStatusTypeId.Online, DateTime.UtcNow.AddMinutes(-5));
        _h.Storage.UpdateStatus(1);
        _h.Storage.SetOffline(1);
        _h.Storage.UpdateStatus(2);
        using var sp = CreateServiceProvider();
        var service = CreateService(sp);

        await InvokeSaveAsync(service, CancellationToken.None);

        var ctx = sp.GetRequiredService<OnlineStatusContext>();
        var statuses = await ctx.UsersOnlineStatuses.ToListAsync();
        statuses.Should().HaveCount(2);
        statuses.Should().Contain(s => s.UserId == 1 && s.Status == DomainStatusTypeId.Offline);
        statuses.Should().Contain(s => s.UserId == 2 && s.Status == DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task SaveStatusesToDatabaseAsync_IncrementsDbRecordsSavedTotal()
    {
        _h.Storage.UpdateStatus(1);
        _h.Storage.UpdateStatus(2);
        using var sp = CreateServiceProvider();
        var service = CreateService(sp);

        await InvokeSaveAsync(service, CancellationToken.None);

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("db_records_saved_total");
        snapshot["db_records_saved_total"].Should().Be(2);
    }

    [Fact]
    public async Task SaveStatusesToDatabaseAsync_EmptyStorage_DoesNothing()
    {
        using var sp = CreateServiceProvider();
        var service = CreateService(sp);

        await InvokeSaveAsync(service, CancellationToken.None);

        var ctx = sp.GetRequiredService<OnlineStatusContext>();
        (await ctx.UsersOnlineStatuses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveStatusesToDatabaseAsync_PreservesInitOnlyProperties()
    {
        var beforeUpdate = DateTime.UtcNow.AddMinutes(-5);
        await _h.SeedDbStatus(1, DomainStatusTypeId.Online, beforeUpdate);
        _h.Storage.UpdateStatus(1);
        _h.Storage.SetOffline(1);
        using var sp = CreateServiceProvider();
        var service = CreateService(sp);

        await InvokeSaveAsync(service, CancellationToken.None);

        var ctx = sp.GetRequiredService<OnlineStatusContext>();
        var status = await ctx.UsersOnlineStatuses.FindAsync(1L);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Offline);
        status.LastSeen.Should().BeAfter(beforeUpdate);
    }

    private DatabasePersistenceService CreateService(IServiceProvider sp)
    {
        return new DatabasePersistenceService(
            sp, _h.Storage, _h.Metrics, TestHelper.CreateLogger<DatabasePersistenceService>());
    }

    private static async Task InvokeSaveAsync(DatabasePersistenceService service, CancellationToken ct)
    {
        await (Task)SaveMethod.Invoke(service, [ct])!;
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<OnlineStatusContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        services.AddSingleton(new OnlineStatusContext(options));
        return services.BuildServiceProvider();
    }
}
