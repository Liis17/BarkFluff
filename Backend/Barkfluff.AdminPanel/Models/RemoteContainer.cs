using LiteDB;

namespace Barkfluff.AdminPanel.Models;

public class RemoteContainer
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string ContainerName { get; set; } = string.Empty;
    public string? ComposeServiceName { get; set; }
    public string? ComposeFiles { get; set; }
    public string? ComposeWorkingDirectory { get; set; }

    [BsonIgnore]
    public bool CanUpdate => !string.IsNullOrWhiteSpace(ComposeServiceName)
        && !string.IsNullOrWhiteSpace(ComposeFiles)
        && !string.IsNullOrWhiteSpace(ComposeWorkingDirectory);
}
