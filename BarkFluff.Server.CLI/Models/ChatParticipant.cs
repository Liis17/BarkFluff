using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Server.CLI.Models
{
    public class ChatParticipant
    {
        public int ChatId { get; set; }
        public int UserId { get; set; }
        public Chat Chat { get; set; }
        public User User { get; set; }

    }
}
