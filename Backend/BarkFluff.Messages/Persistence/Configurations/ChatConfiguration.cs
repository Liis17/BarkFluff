using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.LastMessage);

        builder.Ignore(x => x.CountUnread);

        builder.Ignore(x => x.FirstUnreadMessageId);

        builder.Ignore(x => x.LastActivityAt);

        builder.Ignore(x => x.PrivateInviterUserId);

        builder.Property(x => x.Type)
            .HasDefaultValue(ChatType.Regular);

        builder.Property(x => x.KdfSalt)
            .HasColumnType("bytea");

        builder.Property(x => x.PassphraseVerifier)
            .HasColumnType("bytea");

        builder.Property(x => x.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(x => x.PrivateInviteState)
            .HasDefaultValue(PrivateChatInviteState.Pending);

        builder.HasIndex(x => new { x.Type, x.PrivateUserLowId, x.PrivateUserHighId })
            .IsUnique()
            .HasFilter("\"Type\" = 1 AND \"PrivateUserLowId\" IS NOT NULL AND \"PrivateUserHighId\" IS NOT NULL");

        builder
            .HasMany(x => x.Members)
            .WithOne(m => m.Chat)
            .HasForeignKey(m => m.ChatId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
