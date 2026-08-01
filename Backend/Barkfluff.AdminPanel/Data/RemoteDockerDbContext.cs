using Barkfluff.AdminPanel.Models;

using LiteDB;

namespace Barkfluff.AdminPanel.Data;

public class RemoteDockerDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public RemoteDockerDbContext(string? dbPath = null)
    {
        dbPath ??= Path.Combine(AppContext.BaseDirectory, "db", "remote_docker.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _db = new LiteDatabase(dbPath);
        Servers = _db.GetCollection<RemoteServer>("remote_servers");
        Servers.EnsureIndex(x => x.Name, true);
        Containers = _db.GetCollection<RemoteContainer>("remote_containers");
        Containers.EnsureIndex(x => x.ServerId);
        Containers.EnsureIndex(x => x.ContainerName);
    }

    public ILiteCollection<RemoteServer> Servers { get; }
    public ILiteCollection<RemoteContainer> Containers { get; }

    public void Dispose() => _db.Dispose();
}
