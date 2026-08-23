using Barkfluff.AdminPanel.Models;

using LiteDB;

using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Data;

public class TokenDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public TokenDbContext(IOptions<LiteDbSettings> settings)
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
        Tokens = _db.GetCollection<AuthToken>("tokens");
        Tokens.EnsureIndex(x => x.LastActivity);
        Admins = _db.GetCollection<AdminRecord>("admins");
        Admins.EnsureIndex(x => x.Username);
    }

    public ILiteCollection<AuthToken> Tokens { get; }

    public ILiteCollection<AdminRecord> Admins { get; }

    public void Dispose()
    {
        _db.Dispose();
    }
}

public class LiteDbSettings
{
    public const string SectionName = "LiteDb";
    public string Path { get; set; } = "db/tokens.db";
}
