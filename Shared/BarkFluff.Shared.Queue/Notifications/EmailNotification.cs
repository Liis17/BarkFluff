namespace BarkFluff.Shared.Queue.Notifications;

public class EmailNotification : Notification
{
    public override TransportId TransportId => TransportId.Email;

    public string Title { get; set; }

    public string Address { get; set; }
}