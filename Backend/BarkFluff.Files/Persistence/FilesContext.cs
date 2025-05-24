using BarkFluff.Files.Domain;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class FilesContext : DbContext
{
    
    public FilesContext(DbContextOptions<FilesContext> options) : base(options) { }
    
    public DbSet<UploadFile> UploadedFiles { get; set; }
    
    public DbSet<TempFile> TempFiles { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TempFile>()
            .HasIndex(x => x.OriginalFileId);
    }
}