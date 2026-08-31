using BarkFluff.Settings.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Settings.Persistence.Contexts;

public sealed class SettingsContext : DbContext
{
    public SettingsContext(DbContextOptions<SettingsContext> options) : base(options) { }

    public DbSet<SettingRevision> SettingsHistory => Set<SettingRevision>();

    public DbSet<ReservedName> ReservedNames => Set<ReservedName>();

    public DbSet<SetupState> SetupStates => Set<SetupState>();

    public DbSet<SettingRow> Settings(SettingsScope scope) => Set<SettingRow>(scope.EntityName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var scope in SettingsScopes.All)
        {
            modelBuilder.SharedTypeEntity<SettingRow>(scope.EntityName, entity =>
            {
                entity.ToTable(scope.TableName);
                entity.HasKey(row => row.Key);
                entity.Property(row => row.Key).HasColumnType("text");
                entity.Property(row => row.Value).HasColumnType("text").IsRequired();
                entity.Property(row => row.EditedBy).HasColumnType("text").IsRequired();
                entity.Property(row => row.EditedAt).HasColumnType("timestamp with time zone");
            });
        }

        modelBuilder.Entity<SettingRevision>(entity =>
        {
            entity.ToTable("SettingsHistory");
            entity.HasKey(revision => revision.Id);
            entity.Property(revision => revision.SettingsTable).HasColumnType("text").IsRequired();
            entity.Property(revision => revision.Key).HasColumnType("text").IsRequired();
            entity.Property(revision => revision.PreviousValue).HasColumnType("text").IsRequired();
            entity.Property(revision => revision.NewValue).HasColumnType("text").IsRequired();
            entity.Property(revision => revision.ChangedAt).HasColumnType("timestamp with time zone");
            entity.Property(revision => revision.ChangedBy).HasColumnType("text").IsRequired();
            entity.Property(revision => revision.ChangedFrom).HasColumnType("text").IsRequired();
            entity.Property(revision => revision.ChangeKind).HasColumnType("text").IsRequired();
            entity.HasIndex(revision => new { revision.SettingsTable, revision.Key, revision.ChangedAt, revision.Id })
                .IsDescending(false, false, true, true);
            entity.HasOne<SettingRevision>()
                .WithMany()
                .HasForeignKey(revision => revision.SourceRevisionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReservedName>(entity =>
        {
            entity.ToTable("ReservedNames");
            entity.HasKey(name => name.Name);
            entity.Property(name => name.Name).HasColumnType("text");
        });

        modelBuilder.Entity<SetupState>(entity =>
        {
            entity.ToTable("SetupState");
            entity.HasKey(state => state.Id);
            entity.Property(state => state.CatalogFingerprint).HasColumnType("text").IsRequired();
            entity.Property(state => state.CompletedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(state => state.CompletedBy).HasColumnType("text").IsRequired();
            entity.Property(state => state.CompletedFrom).HasColumnType("text").IsRequired();
        });
    }
}
