using BarkFluff.Federation.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Persistence.Contexts;

public class FederationContext : DbContext
{
    public DbSet<FederationSigningKey> SigningKeys { get; set; }

    public FederationContext(DbContextOptions<FederationContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FederationSigningKey>(entity =>
        {
            entity.HasKey(e => e.KeyId);
            entity.Property(e => e.KeyId).ValueGeneratedNever();
            entity.Property(e => e.PublicKey).IsRequired();
            entity.Property(e => e.PrivateKeySeed).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });
    }
}
