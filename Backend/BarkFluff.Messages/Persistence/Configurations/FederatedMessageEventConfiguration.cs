using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class FederatedMessageEventConfiguration : IEntityTypeConfiguration<FederatedMessageEvent>
{
    public void Configure(EntityTypeBuilder<FederatedMessageEvent> builder)
    {
        builder.HasKey(x => new { x.ChatId, x.FederatedId });

        builder.Property(x => x.EventBytes).HasColumnType("bytea");
    }
}
