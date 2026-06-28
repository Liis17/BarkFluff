using BarkFluff.Calls.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Calls.Persistence.Configurations;

public class CallSessionConfiguration : IEntityTypeConfiguration<CallSession>
{
    public void Configure(EntityTypeBuilder<CallSession> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.RoomName).IsRequired();

        builder.Ignore(c => c.IsGroup);
        builder.Ignore(c => c.DurationSeconds);

        // Поиск активных входящих звонков получателя (ring/таймаут) и истории.
        builder.HasIndex(c => c.CalleeUserId);
        builder.HasIndex(c => c.ChatId);
        builder.HasIndex(c => c.Status);
    }
}
