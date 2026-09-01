using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence;

public class MessagesContext : DbContext
{
    public MessagesContext(DbContextOptions<MessagesContext> options) : base(options) { }

    public DbSet<Chat> Chats { get; set; }

    public DbSet<Message> Messages { get; set; }

    public DbSet<MessageOutboxEntry> MessageOutbox { get; set; }

    public DbSet<ChatMember> ChatMembers { get; set; }

    public DbSet<GroupChatInfo> GroupChatInfos { get; set; }

    public DbSet<MessageAttachment> MessageAttachments { get; set; }

    public DbSet<PinnedMessage> PinnedMessages { get; set; }

    public DbSet<EncryptedMessage> EncryptedMessages { get; set; }

    public DbSet<PrivateChatReadState> PrivateChatReadStates { get; set; }

    public DbSet<FederatedMessageEvent> FederatedMessageEvents { get; set; }

    public DbSet<FederatedReadState> FederatedReadStates { get; set; }

    public DbSet<ChatDraft> ChatDrafts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ChatConfiguration());
        modelBuilder.ApplyConfiguration(new ChatMemberConfiguration());
        modelBuilder.ApplyConfiguration(new MessageConfiguration());
        modelBuilder.ApplyConfiguration(new MessageOutboxConfiguration());
        modelBuilder.ApplyConfiguration(new PinnedMessageConfiguration());
        modelBuilder.ApplyConfiguration(new EncryptedMessageConfiguration());
        modelBuilder.ApplyConfiguration(new PrivateChatReadStateConfiguration());
        modelBuilder.ApplyConfiguration(new FederatedMessageEventConfiguration());
        modelBuilder.ApplyConfiguration(new FederatedReadStateConfiguration());
        modelBuilder.ApplyConfiguration(new ChatDraftConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
