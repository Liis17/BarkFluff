using BarkFluff.Shared.Identity;

namespace BarkFluff.Shared.Queue.Notifications;

public abstract class Notification
{
    public abstract TransportId TransportId { get; }
    
    public ServiceId ServiceId { get; set; }
    
    public long? OwnerId { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public NotificationType Type { get; set; }
    
    public Dictionary<string, string> Payload { get; set; }
}