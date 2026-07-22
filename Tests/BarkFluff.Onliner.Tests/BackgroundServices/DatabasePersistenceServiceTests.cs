using System.Reflection;

using BarkFluff.Onliner.BackgroundServices;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Onliner.Tests.BackgroundServices;

public class DatabasePersistenceServiceTests
{
    private static readonly MethodInfo SaveMethod =
        typeof(DatabasePersistenceService).GetMethod("SaveStatusesToDatabaseAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly TestHelper _h = new();

    private DatabasePersistenceService CreateService()
    {
        return new DatabasePersistenceService(
            _h.CreateScopeFactory(),
            _h.Presence,
            TestHelper.CreateSingleRunner(),
            _h.Metrics,
            TestHelper.CreateLogger<DatabasePersistenceService>());
    }

    private static Task InvokeSaveAsync(DatabasePersistenceService service)
        => (Task)SaveMethod.Invoke(service, [CancellationToken.None])!;

    [Fact]
    public async Task Save_InsertsNewOnlineRecords()
    {
        await _h.Presence.MarkOnlineAsync(1);
        await _h.Presence.MarkOnlineAsync(2);

        await InvokeSaveAsync(CreateService());

        var statuses = await _h.DbContext.UsersOnlineStatuses.ToListAsync();
        statuses.Should().HaveCount(2);
        statuses.Should().Contain(s => s.UserId == 1 && s.Status == DomainStatusTypeId.Online);
        statuses.Should().Contain(s => s.UserId == 2 && s.Status == DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task Save_UpdatesExistingRecord()
    {
        var oldSeen = DateTime.UtcNow.AddMinutes(-5);
        await _h.SeedDbStatus(1, DomainStatusTypeId.Online, oldSeen);
        await _h.Presence.MarkOnlineAsync(1);

        await InvokeSaveAsync(CreateService());

        var status = await _h.DbContext.UsersOnlineStatuses.FindAsync(1L);
        status.Should().NotBeNull();
        status!.Status.Should().Be(DomainStatusTypeId.Online);
        status.LastSeen.Should().BeAfter(oldSeen);
    }

    [Fact]
    public async Task Save_MixedInsertAndUpdate()
    {
        await _h.SeedDbStatus(1, DomainStatusTypeId.Online, DateTime.UtcNow.AddMinutes(-5));
        await _h.Presence.MarkOnlineAsync(1);
        await _h.Presence.MarkOnlineAsync(2);

        await InvokeSaveAsync(CreateService());

        var statuses = await _h.DbContext.UsersOnlineStatuses.ToListAsync();
        statuses.Should().HaveCount(2);
        statuses.Should().Contain(s => s.UserId == 1 && s.Status == DomainStatusTypeId.Online);
        statuses.Should().Contain(s => s.UserId == 2 && s.Status == DomainStatusTypeId.Online);
    }

    [Fact]
    public async Task Save_IncrementsDbRecordsSavedTotal()
    {
        await _h.Presence.MarkOnlineAsync(1);
        await _h.Presence.MarkOnlineAsync(2);

        await InvokeSaveAsync(CreateService());

        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("db_records_saved_total");
        snapshot["db_records_saved_total"].Should().Be(2);
    }

    [Fact]
    public async Task Save_EmptyPresence_DoesNothing()
    {
        await InvokeSaveAsync(CreateService());

        (await _h.DbContext.UsersOnlineStatuses.CountAsync()).Should().Be(0);
    }
}
