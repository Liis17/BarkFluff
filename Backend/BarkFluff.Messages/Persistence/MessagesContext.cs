using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence;

public class MessagesContext : DbContext
{
    public MessagesContext(DbContextOptions<MessagesContext> options) : base(options) { }

    public DbSet<Chat> Chats { get; set; }

    public DbSet<Message> Messages { get; set; }

    public DbSet<ChatMember> ChatMembers { get; set; }

    public DbSet<GroupChatInfo> GroupChatInfos { get; set; }

    public DbSet<MessageAttachment> MessageAttachments { get; set; }

    public DbSet<PinnedMessage> PinnedMessages { get; set; }

    public DbSet<EncryptedMessage> EncryptedMessages { get; set; }

    public DbSet<PrivateChatReadState> PrivateChatReadStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ChatConfiguration());
        modelBuilder.ApplyConfiguration(new ChatMemberConfiguration());
        modelBuilder.ApplyConfiguration(new MessageConfiguration());
        modelBuilder.ApplyConfiguration(new PinnedMessageConfiguration());
        modelBuilder.ApplyConfiguration(new EncryptedMessageConfiguration());
        modelBuilder.ApplyConfiguration(new PrivateChatReadStateConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
