using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class FederatedReadStateConfiguration : IEntityTypeConfiguration<FederatedReadState>
{
    public void Configure(EntityTypeBuilder<FederatedReadState> builder)
    {
        builder.HasKey(x => new { x.ChatId, x.UserUuid });
    }
}
