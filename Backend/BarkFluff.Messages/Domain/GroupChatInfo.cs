using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Messages.Domain;

public class GroupChatInfo
{
    [Key]
    public long Id { get; set; }
    
    public Guid ChatId { get; set; }
    
    public long Creator { get; set; }
    
    public List<long> UsersCanKick { get; set; }
    
    public DateTime CreatedAt { get; set; }
}