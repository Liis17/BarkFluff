namespace Barkfluff.AdminPanel.Models.Dtos;

public class SaveRemoteServerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public record RemoteServerDto(Guid Id, string Name, string Host, int Port, string Username,
    bool IsPasswordConfigured, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record AddRemoteContainerRequest(string ContainerName);

public class RemoteContainerStatusDto
{
    public Guid Id { get; set; }
    public string ContainerName { get; set; } = string.Empty;
    public string? ComposeServiceName { get; set; }
    public bool CanUpdate { get; set; }
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class DiscoveredRemoteContainerDto
{
    public string Id { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsComposeManaged { get; set; }
}
