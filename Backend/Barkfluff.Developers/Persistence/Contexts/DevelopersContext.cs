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
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Order).HasColumnName("order");
            e.Property(x => x.Content).HasColumnName("content").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<ProtoMetadata>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FileName).HasColumnName("file_name");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.Order).HasColumnName("order");
            e.Property(x => x.RpcDescriptions).HasColumnName("rpc_descriptions").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.FileName).IsUnique();
        });

        modelBuilder.Entity<ErrorCodeEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.ExceptionName).HasColumnName("exception_name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Domain).HasColumnName("domain");
            e.HasIndex(x => x.Code).IsUnique();
        });
    }
}
