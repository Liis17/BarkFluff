using System.Text.Json;

using BarkFluff.Navigator.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Navigator.Persistence;

public class NavigatorContext : DbContext
{
    public DbSet<ServerInfo> Servers { get; set; }

    public NavigatorContext(DbContextOptions<NavigatorContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServerInfo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.ServerName)
                .IsUnique()
                .HasFilter("\"ServerName\" IS NOT NULL");

            entity.Property(e => e.SigningKeys)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => v == null ? null : JsonSerializer.Deserialize<List<NavigatorSigningKeyInfo>>(v, (JsonSerializerOptions?)null))
                .HasColumnType("jsonb");
        });
    }
}
