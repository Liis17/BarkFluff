using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

[Collection("LiteDb")]
public sealed class AuditServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"adminpanel-audit-{Guid.NewGuid():N}.db");
    private readonly AuditDbContext _db;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _db = new AuditDbContext(Microsoft.Extensions.Options.Options.Create(new AuditDbSettings { Path = _dbPath }));
        _service = new AuditService(_db, NullLogger<AuditService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void Log_PersistsEntry()
    {
        _service.Log(new AuditLogEntry
        {
            AdminUsername = "alice",
            TelegramUserId = 100,
            Action = "docker.branch",
            Details = "Переключение ветки обновлений",
            IpAddress = "127.0.0.1",
            ConfirmationId = "abc",
            Outcome = "confirmed"
        });

        var entries = _service.GetEntries(10, beforeUtc: null);

        var entry = Assert.Single(entries);
        Assert.Equal("alice", entry.AdminUsername);
        Assert.Equal("docker.branch", entry.Action);
        Assert.Equal("confirmed", entry.Outcome);
        Assert.Equal("abc", entry.ConfirmationId);
    }

    [Fact]
    public void GetEntries_ReturnsNewestFirstAndRespectsLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            _service.Log(new AuditLogEntry { Action = $"action-{i}", At = DateTime.UtcNow.AddMinutes(i) });
        }

        var entries = _service.GetEntries(3, beforeUtc: null);

        Assert.Equal(3, entries.Count);
        Assert.Equal("action-4", entries[0].Action);
        Assert.Equal("action-2", entries[2].Action);
    }

    private static readonly DateTime BaseTime = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private void LogEntry(string? admin, string action, string outcome, DateTime at)
    {
        _service.Log(new AuditLogEntry
        {
            AdminUsername = admin,
            Action = action,
            Outcome = outcome,
            At = at
        });
    }

    [Fact]
    public void GetEntries_FiltersByAdminExactlyAndIgnoringCaseAndLeadingAt()
    {
        LogEntry("Alice", "docker.branch", "ok", BaseTime);
        LogEntry("alice2", "docker.branch", "ok", BaseTime.AddMinutes(1));
        LogEntry("bob", "docker.branch", "ok", BaseTime.AddMinutes(2));

        var entries = _service.GetEntries(10, null, new AuditEntryFilter(AdminUsername: "@ALICE"));

        var entry = Assert.Single(entries);
        Assert.Equal("Alice", entry.AdminUsername);
    }

    [Fact]
    public void GetEntries_FiltersByActionSubstringIgnoringCase()
    {
        LogEntry("alice", "docker.branch", "ok", BaseTime);
        LogEntry("alice", "users.block", "ok", BaseTime.AddMinutes(1));
        LogEntry("alice", "Docker.restart", "ok", BaseTime.AddMinutes(2));

        var entries = _service.GetEntries(10, null, new AuditEntryFilter(Action: "DOCKER"));

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Contains("docker", e.Action, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetEntries_TreatsActionFilterAsLiteralText()
    {
        LogEntry("alice", "docker.branch", "ok", BaseTime);

        Assert.Empty(_service.GetEntries(10, null, new AuditEntryFilter(Action: "%")));
        Assert.Empty(_service.GetEntries(10, null, new AuditEntryFilter(Action: "_")));
    }

    [Fact]
    public void GetEntries_OutcomeSuccessMatchesOkAndConfirmed()
    {
        LogEntry("alice", "a", "ok", BaseTime);
        LogEntry("alice", "b", "confirmed", BaseTime.AddMinutes(1));
        LogEntry("alice", "c", "invalid_confirmation", BaseTime.AddMinutes(2));
        LogEntry("alice", "d", "approved", BaseTime.AddMinutes(3));

        var success = _service.GetEntries(10, null, new AuditEntryFilter(Outcome: "success"));
        Assert.Equal(new[] { "b", "a" }, success.Select(e => e.Action));

        var invalid = _service.GetEntries(10, null, new AuditEntryFilter(Outcome: "invalid_confirmation"));
        Assert.Equal("c", Assert.Single(invalid).Action);

        var approved = _service.GetEntries(10, null, new AuditEntryFilter(Outcome: "approved"));
        Assert.Equal("d", Assert.Single(approved).Action);
    }

    [Fact]
    public void GetEntries_PeriodFromInclusiveToExclusive()
    {
        LogEntry("alice", "t0", "ok", BaseTime);
        LogEntry("alice", "t1", "ok", BaseTime.AddMinutes(1));
        LogEntry("alice", "t2", "ok", BaseTime.AddMinutes(2));

        var entries = _service.GetEntries(10, null, new AuditEntryFilter(
            FromUtc: BaseTime.AddMinutes(1),
            ToUtc: BaseTime.AddMinutes(2)));

        Assert.Equal("t1", Assert.Single(entries).Action);
    }

    [Fact]
    public void GetEntries_CombinesAllFilters()
    {
        LogEntry("alice", "docker.branch", "ok", BaseTime);
        LogEntry("alice", "docker.branch", "invalid_confirmation", BaseTime.AddMinutes(1));
        LogEntry("alice", "users.block", "ok", BaseTime.AddMinutes(2));
        LogEntry("bob", "docker.branch", "ok", BaseTime.AddMinutes(3));
        LogEntry("alice", "docker.branch", "ok", BaseTime.AddMinutes(10));

        var entries = _service.GetEntries(10, null, new AuditEntryFilter(
            AdminUsername: "alice",
            Action: "docker",
            Outcome: "success",
            FromUtc: BaseTime,
            ToUtc: BaseTime.AddMinutes(5)));

        Assert.Equal(BaseTime, Assert.Single(entries).At.ToUniversalTime());
    }

    [Fact]
    public void GetEntries_AppliesFiltersBeforeLimit()
    {
        // Три подходящие записи старее 60 свежих посторонних: без фильтрации до лимита страница была бы пустой.
        for (var i = 0; i < 3; i++)
        {
            LogEntry("alice", "docker.branch", "ok", BaseTime.AddMinutes(i));
        }

        for (var i = 0; i < 60; i++)
        {
            LogEntry("bob", "users.block", "ok", BaseTime.AddMinutes(100 + i));
        }

        var page = _service.GetEntries(2, null, new AuditEntryFilter(AdminUsername: "alice"));

        Assert.Equal(2, page.Count);
        Assert.Equal(BaseTime.AddMinutes(2), page[0].At.ToUniversalTime());
        Assert.Equal(BaseTime.AddMinutes(1), page[1].At.ToUniversalTime());
    }

    [Fact]
    public void GetEntries_ActionFilterScansPastFirstBatchOfUnmatchedEntries()
    {
        LogEntry("alice", "docker.branch", "ok", BaseTime);
        LogEntry("alice", "docker.restart", "ok", BaseTime.AddMinutes(1));
        for (var i = 0; i < 600; i++)
        {
            LogEntry("bob", "users.block", "ok", BaseTime.AddMinutes(100 + i));
        }

        var entries = _service.GetEntries(5, null, new AuditEntryFilter(Action: "docker"));

        Assert.Equal(new[] { "docker.restart", "docker.branch" }, entries.Select(e => e.Action));
    }

    [Fact]
    public void GetEntries_KeysetWithFilterContinuesFilteredSequenceWithoutGapsOrDuplicates()
    {
        for (var i = 0; i < 50; i++)
        {
            var admin = i % 2 == 0 ? "alice" : "bob";
            LogEntry(admin, $"action-{i}", "ok", BaseTime.AddSeconds(i));
        }

        var expected = Enumerable.Range(0, 25).Select(i => $"action-{i * 2}").Reverse().ToList();
        var collected = new List<string>();
        DateTime? before = null;
        var filter = new AuditEntryFilter(AdminUsername: "alice");

        for (var pageIndex = 0; pageIndex < 20; pageIndex++)
        {
            var page = _service.GetEntries(4, before, filter);
            if (page.Count == 0)
            {
                break;
            }

            collected.AddRange(page.Select(e => e.Action));
            before = page[^1].At;
        }

        Assert.Equal(expected, collected);
    }

    [Fact]
    public void ValidateFilter_RejectsInvertedPeriodAndOverlongValues()
    {
        Assert.Null(AuditService.ValidateFilter(new AuditEntryFilter(FromUtc: BaseTime, ToUtc: BaseTime)));
        Assert.Null(AuditService.ValidateFilter(new AuditEntryFilter(AdminUsername: "alice")));

        Assert.NotNull(AuditService.ValidateFilter(new AuditEntryFilter(
            FromUtc: BaseTime.AddDays(1),
            ToUtc: BaseTime)));

        Assert.NotNull(AuditService.ValidateFilter(new AuditEntryFilter(
            Action: new string('a', AuditService.MaxFilterLength + 1))));
    }

    [Fact]
    public void GetFacets_ReturnsDistinctAdminsAndActionsFromHistory()
    {
        LogEntry("Alice", "docker.branch", "ok", BaseTime);
        LogEntry("alice", "docker.branch", "ok", BaseTime.AddMinutes(1));
        LogEntry("bob", "users.block", "ok", BaseTime.AddMinutes(2));
        LogEntry(null, "users.block", "ok", BaseTime.AddMinutes(3));

        var (admins, actions) = _service.GetFacets();

        // Регистр не различает администратора: Alice и alice — одна подсказка.
        Assert.Equal(2, admins.Count);
        Assert.Contains(admins, a => string.Equals(a, "alice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("bob", admins);
        Assert.Equal(new[] { "docker.branch", "users.block" }, actions);
    }
}
