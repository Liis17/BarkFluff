using Barkfluff.AdminPanel.Models;

using LiteDB;

using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Data;

public class AuditDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public AuditDbContext(IOptions<AuditDbSettings> settings)
    {
        var dbPath = settings.Value.Path;
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);
        }

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _db = new LiteDatabase(dbPath);
        Entries = _db.GetCollection<AuditLogEntry>("audit_log");
        Entries.EnsureIndex(x => x.At);
    }

    public ILiteCollection<AuditLogEntry> Entries { get; }

    public void Dispose()
    {
        _db.Dispose();
    }
}

public class AuditDbSettings
{
    public const string SectionName = "AuditDb";
    public string Path { get; set; } = "db/audit.db";
}
