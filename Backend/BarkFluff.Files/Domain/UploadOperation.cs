using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Files.Domain;

public class UploadOperation
{
    [Key]
    public Guid Id { get; set; }

    public Guid? ClientOperationId { get; set; }

    public long UserId { get; set; }

    public Guid ReservedFileId { get; set; }

    public Guid? ResultFileId { get; set; }

    public UploadFileType Type { get; set; }

    public UploadOperationState State { get; set; } = UploadOperationState.Pending;

    public Guid? LeaseToken { get; set; }

    public DateTime? LeaseExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum UploadOperationState
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
}
