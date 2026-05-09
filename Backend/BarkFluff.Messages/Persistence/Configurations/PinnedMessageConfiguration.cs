using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class PinnedMessageConfiguration : IEntityTypeConfiguration<PinnedMessage>
{
    public void Configure(EntityTypeBuilder<PinnedMessage> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ChatId, x.MessageId }).IsUnique();

        builder.HasIndex(x => x.ChatId);

        builder
            .HasOne<Chat>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
