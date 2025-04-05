namespace BarkFluff.Server.CLI.Models
{
    public class Message
    {
        public int MessageId { get; set; }
        public int ChatId { get; set; }
        public Chat Chat { get; set; }
        public int SenderId { get; set; }
        public User Sender { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
