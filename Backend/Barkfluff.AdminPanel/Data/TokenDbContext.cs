using Barkfluff.AdminPanel.Models;

using LiteDB;

using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Data;

public class TokenDbContext : IDisposable
{
    private static readonly object LiteDbInitializationLock = new();
    private readonly LiteDatabase _db;
    private readonly object _transactionLock = new();

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

        lock (LiteDbInitializationLock)
        {
            _db = new LiteDatabase(dbPath);
            Tokens = _db.GetCollection<AuthToken>("tokens");
            Tokens.EnsureIndex(x => x.LastActivity);
            Admins = _db.GetCollection<AdminRecord>("admins");
            Admins.EnsureIndex(x => x.Username);
            AdminInvitations = _db.GetCollection<AdminInvitation>("admin_invitations");
            AdminInvitations.EnsureIndex(x => x.Payload, unique: true);
            AdminInvitations.EnsureIndex(x => x.TelegramUserId);
        }
    }

    public ILiteCollection<AuthToken> Tokens { get; }

    public ILiteCollection<AdminRecord> Admins { get; }

    public ILiteCollection<AdminInvitation> AdminInvitations { get; }

    public bool RunInTransaction(Func<bool> operation)
    {
        lock (_transactionLock)
        {
            var ownsTransaction = _db.BeginTrans();
            try
            {
                var succeeded = operation();
                if (!ownsTransaction)
                    return succeeded;

                if (succeeded)
                {
                    _db.Commit();
                    return true;
                }

                _db.Rollback();
                return false;
            }
            catch
            {
                if (ownsTransaction)
                    _db.Rollback();
                throw;
            }
        }
    }

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
