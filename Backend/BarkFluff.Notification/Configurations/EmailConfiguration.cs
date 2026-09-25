namespace BarkFluff.Notification.Configurations;

public class EmailConfiguration
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; }

    public int Port { get; set; }

    public string SenderEmail { get; set; }

    public string SenderPassword { get; set; }
}
