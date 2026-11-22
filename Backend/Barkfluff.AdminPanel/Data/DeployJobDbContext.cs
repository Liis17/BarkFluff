using Barkfluff.AdminPanel.Models;

using LiteDB;

namespace Barkfluff.AdminPanel.Data;

/// <summary>
/// История задач деплоя. Отдельный файл рядом с другими данными панели (db/ по умолчанию);
/// путь не настраивается, как у <see cref="RemoteDockerDbContext"/>, поэтому новая конфигурация не нужна.
/// </summary>
public class DeployJobDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public DeployJobDbContext(string? dbPath = null)
    {
        dbPath ??= Path.Combine(AppContext.BaseDirectory, "db", "deploy_jobs.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _db = new LiteDatabase(dbPath);
        Jobs = _db.GetCollection<DeployJobRecord>("deploy_jobs");
    }

    public ILiteCollection<DeployJobRecord> Jobs { get; }

    public void Dispose() => _db.Dispose();
}
