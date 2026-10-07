using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public class RemoteSshQuickAction
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Commands { get; set; } = string.Empty;
}
