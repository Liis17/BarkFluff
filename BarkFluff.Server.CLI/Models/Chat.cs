namespace BarkFluff.Server.CLI.Models
{
    public class Chat
    {
        public int ChatId { get; set; }
        public string Name { get; set; } // Название для групповых чатов
        public ICollection<ChatParticipant> ChatParticipants { get; set; }
        public ICollection<Message> Messages { get; set; }
    }
}
