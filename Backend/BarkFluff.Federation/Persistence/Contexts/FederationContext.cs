using BarkFluff.Federation.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Persistence.Contexts;

public class FederationContext : DbContext
{
    public DbSet<FederationSigningKey> SigningKeys { get; set; }

    public DbSet<KnownServer> KnownServers { get; set; }

    public DbSet<KnownServerKey> KnownServerKeys { get; set; }

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

        modelBuilder.Entity<KnownServer>(entity =>
        {
            entity.HasKey(e => e.ServerName);
            entity.Property(e => e.ServerName).ValueGeneratedNever();
            entity.Property(e => e.FederationEndpoint).IsRequired();
            entity.Property(e => e.Source).HasConversion<string>().IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().IsRequired();
            entity.Property(e => e.FirstSeenAt).IsRequired();
            entity.Property(e => e.LastSeenAt).IsRequired();
            entity.Property(e => e.ProtocolVersion).IsRequired();

            entity.HasMany(e => e.Keys)
                .WithOne(k => k.Server)
                .HasForeignKey(k => k.ServerName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnownServerKey>(entity =>
        {
            entity.HasKey(e => new { e.ServerName, e.KeyId });
            entity.Property(e => e.PublicKey).IsRequired();
        });
    }
}
