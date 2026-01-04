using System.ComponentModel.DataAnnotations;
using BarkFluff.Onliner.Domain.Enums;

namespace BarkFluff.Onliner.Domain.Entities;

public class UserOnlineStatus
{
    [Key]
    public long UserId { get; set; }
    
    public StatusTypeId Status { get; set; }
    
    public DateTime LastSeen { get; set; }
}