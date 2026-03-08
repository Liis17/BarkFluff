namespace BarkFluff.Notification.Configurations;

public class EmailConfiguration
{
    public string Host { get; set; }

    public int Port { get; set; }

    public string SenderEmail { get; set; }

    public string SenderPassword { get; set; }
}