using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Domain;

public class TempFile
{
    public Guid Id { get; set; }
    
    public Guid OriginalFileId { get; set; }
    
    public DateTime ExpiresAt { get; set; }
}