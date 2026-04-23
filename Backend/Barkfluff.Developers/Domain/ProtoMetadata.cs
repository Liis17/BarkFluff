namespace Barkfluff.Developers.Domain;

public class ProtoMetadata
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int Order { get; set; }
    public string RpcDescriptions { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
