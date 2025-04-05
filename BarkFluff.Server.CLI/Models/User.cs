namespace BarkFluff.Server.CLI.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public ICollection<ChatParticipant> ChatParticipants { get; set; }
    }
}
