using Barkfluff.AdminPanel.Models;

using LiteDB;

namespace Barkfluff.AdminPanel.Data;

/// <summary>
/// История задач деплоя. Отдельный файл рядом с другими данными панели (db/ по умолчанию);
/// путь не настраивается, как у <see cref="RemoteDockerDbContext"/>, поэтому новая конфигурация не нужна.
/// </summary>
public class DeployJobDbContext : IDisposable
{
    private readonly string _dbPath;
    private readonly object _sync = new();
    private LiteDatabase? _db;

    public DeployJobDbContext(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "db", "deploy_jobs.db");
    }

    /// <summary>
    /// Коллекция истории. Файл открывается при первом обращении, а не в конструкторе:
    /// ошибка открытия не должна мешать запуску панели, а сервис логирует её как предупреждение.
    /// </summary>
    public ILiteCollection<DeployJobRecord> Jobs
    {
        get
        {
            lock (_sync)
            {
                _db ??= Open();
                return _db.GetCollection<DeployJobRecord>("deploy_jobs");
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _db?.Dispose();
            _db = null;
        }
    }

    private LiteDatabase Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        return new LiteDatabase(_dbPath);
    }
}
