namespace BarkFluff.ClientStorage.Domain;

public class ClientFile
{
    public int Id { get; set; }

    public ClientType ClientType { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string S3Key { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }
}
