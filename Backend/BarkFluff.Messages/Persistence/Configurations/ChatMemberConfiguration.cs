using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ChatId, x.UserId });
        builder.HasIndex(x => new { x.UserId, x.ChatId })
            .HasFilter("\"UserId\" IS NOT NULL")
            .IsCreatedConcurrently();
    }
}
