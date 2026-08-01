using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class ChatDraftConfiguration : IEntityTypeConfiguration<ChatDraft>
{
    public void Configure(EntityTypeBuilder<ChatDraft> builder)
    {
        builder.HasKey(x => new { x.ChatId, x.UserId });

        builder.Property(x => x.Text)
            .HasMaxLength(4096)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new { x.UserId, x.ChatId });

        builder.HasOne(x => x.Chat)
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
