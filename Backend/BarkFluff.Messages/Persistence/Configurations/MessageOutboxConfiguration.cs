using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class MessageOutboxConfiguration : IEntityTypeConfiguration<MessageOutboxEntry>
{
    public void Configure(EntityTypeBuilder<MessageOutboxEntry> builder)
    {
        builder.ToTable("MessageOutbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Payload).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.EventKind).HasConversion<int>().HasDefaultValue(MessageOutboxEventKind.NewMessage);
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => x.MessageId).IsUnique().HasFilter("\"EventKind\" = 0");
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.Status, x.EventKind, x.Id, x.NextAttemptAt })
            .HasDatabaseName("IX_MessageOutbox_Status_EventKind_Id_NextAttemptAt");
    }
}
