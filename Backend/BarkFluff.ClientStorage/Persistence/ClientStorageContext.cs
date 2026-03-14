using BarkFluff.ClientStorage.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.ClientStorage.Persistence;

public class ClientStorageContext : DbContext
{
    public ClientStorageContext(DbContextOptions<ClientStorageContext> options) : base(options) { }

    public DbSet<ClientFile> ClientFiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientType);
            entity.Property(e => e.OriginalFileName).IsRequired();
            entity.Property(e => e.S3Key).IsRequired();
            entity.Property(e => e.Checksum).IsRequired();
        });
    }
}
