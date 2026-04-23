using Barkfluff.Developers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Barkfluff.Developers.Persistence.Contexts;

public class DevelopersContext : DbContext
{
    public DevelopersContext(DbContextOptions<DevelopersContext> options) : base(options) { }

    public DbSet<DocumentationSection> DocumentationSections => Set<DocumentationSection>();
    public DbSet<ProtoMetadata> ProtoMetadata => Set<ProtoMetadata>();
    public DbSet<ErrorCodeEntry> ErrorCodes => Set<ErrorCodeEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentationSection>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Content).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ProtoMetadata>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FileName).IsUnique();
            e.Property(x => x.RpcDescriptions).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ErrorCodeEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });
    }
}
