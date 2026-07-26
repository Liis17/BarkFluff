using BarkFluff.Federation.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Persistence.Contexts;

public class FederationContext : DbContext
{
    public DbSet<FederationSigningKey> SigningKeys { get; set; }

    public DbSet<KnownServer> KnownServers { get; set; }

    public DbSet<KnownServerKey> KnownServerKeys { get; set; }

    public DbSet<FederationOutbox> Outbox { get; set; }

    public DbSet<ProcessedEvent> ProcessedEvents { get; set; }

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

        modelBuilder.Entity<FederationOutbox>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Destination).IsRequired();
            entity.Property(e => e.EventId).IsRequired();
            entity.Property(e => e.EventType).IsRequired();
            entity.Property(e => e.PayloadBytes).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.NextAttemptAt).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>().IsRequired();
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt })
                .HasDatabaseName("IX_Outbox_Status_NextAttemptAt");
            entity.HasIndex(e => new { e.Destination, e.ChatId, e.Id })
                .HasDatabaseName("IX_Outbox_Destination_ChatId_Id");
            entity.ToTable("FederationOutbox");
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.OriginServer).IsRequired();
            entity.Property(e => e.ReceivedAt).IsRequired();
            entity.ToTable("ProcessedEvents");
        });
    }
}
