using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Services;

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
}
