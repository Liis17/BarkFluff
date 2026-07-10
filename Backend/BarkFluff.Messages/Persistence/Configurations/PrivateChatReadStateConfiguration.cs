using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class PrivateChatReadStateConfiguration : IEntityTypeConfiguration<PrivateChatReadState>
{
    public void Configure(EntityTypeBuilder<PrivateChatReadState> builder)
    {
        builder.HasKey(x => new { x.ChatId, x.UserId });

        builder.HasOne<Chat>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
