using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class EncryptedMessageConfiguration : IEntityTypeConfiguration<EncryptedMessage>
{
    public void Configure(EntityTypeBuilder<EncryptedMessage> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Ciphertext).HasColumnType("bytea");
        builder.Property(x => x.Nonce).HasColumnType("bytea");
        builder.Property(x => x.AssociatedData).HasColumnType("bytea");

        builder.Property(x => x.IsEdited).HasDefaultValue(false);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(x => new { x.ChatId, x.SentAt });
        builder.HasIndex(x => x.ChatId);
    }
}
