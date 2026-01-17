using LiteDB;
using Barkfluff.Docker.Control.Models;
using Microsoft.Extensions.Options;

namespace Barkfluff.Docker.Control.Data;

public class TokenDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public TokenDbContext(IOptions<LiteDbSettings> settings)
    {
        // Если путь относительный, делаем его относительно папки исполняемого файла
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
    }

    public ILiteCollection<AuthToken> Tokens { get; }

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
