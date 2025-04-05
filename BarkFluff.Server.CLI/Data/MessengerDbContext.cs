using BarkFluff.Server.CLI.Models;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Server.CLI.Data
{
    public class MessengerDbContext : DbContext
    {
        public DbSet<Models.User> Users { get; set; }
        public DbSet<Models.Chat> Chats { get; set; }
        public DbSet<Models.ChatParticipant> ChatParticipants { get; set; }
        public DbSet<Models.Message> Messages { get; set; }

        public MessengerDbContext(DbContextOptions<MessengerDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ChatParticipant>()
                .HasKey(cp => new { cp.ChatId, cp.UserId });
        }
    }
}

    
