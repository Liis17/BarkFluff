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

        builder
            .HasMany(x => x.Members)
            .WithOne(m => m.Chat)
            .HasForeignKey(m => m.ChatId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}