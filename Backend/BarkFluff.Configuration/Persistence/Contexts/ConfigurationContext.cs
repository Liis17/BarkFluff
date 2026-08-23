using BarkFluff.Configuration.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Configuration.Persistence;

public class ConfigurationContext : DbContext
{
    public ConfigurationContext(DbContextOptions<ConfigurationContext> options) : base(options) { }

    public DbSet<ConfigurationItem> Configurations { get; set; }

    public DbSet<ConfigurationRevision> ConfigurationRevisions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConfigurationRevision>()
            .HasOne(x => x.ConfigurationItem)
            .WithMany()
            .HasForeignKey(x => x.ConfigurationItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConfigurationRevision>()
            .HasIndex(x => new { x.ServiceId, x.Section, x.Key, x.ChangedAt });
    }
}
