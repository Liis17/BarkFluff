using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class FilesContext : DbContext
{

    public FilesContext(DbContextOptions<FilesContext> options) : base(options) { }

    public DbSet<UploadFile> UploadedFiles { get; set; }

    public DbSet<TempFile> TempFiles { get; set; }

    public DbSet<FileHash> FileHashes { get; set; }

    public DbSet<BadgeImage> BadgeImages { get; set; }

    public DbSet<StickerPack> StickerPacks { get; set; }

    public DbSet<Sticker> Stickers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TempFile>()
            .HasIndex(x => x.OriginalFileId);

        // Configure FileHashes with index on Hash for fast lookups
        modelBuilder.Entity<FileHash>()
            .HasIndex(x => x.Hash);

        modelBuilder.Entity<StickerPack>()
            .HasIndex(x => x.CreatorUserId);

        modelBuilder.Entity<Sticker>()
            .HasIndex(x => x.StickerPackId);

        modelBuilder.Entity<Sticker>()
            .HasOne(s => s.StickerPack)
            .WithMany(sp => sp.Stickers)
            .HasForeignKey(s => s.StickerPackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}