using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Users.Domain;

public class ChatFolder
{
    [Key]
    public long Id { get; set; }

    public long OwnerUserId { get; set; }

    public User? User { get; set; }

    public Guid FolderId { get; set; }

    public string FolderName { get; set; } = string.Empty;

    public string? FolderIcon { get; set; }

    public long[] ChatList { get; set; } = [];

    public int SortOrder { get; set; }
}
