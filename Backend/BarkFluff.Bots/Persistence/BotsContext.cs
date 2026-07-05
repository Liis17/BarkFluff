using BarkFluff.Bots.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Bots.Persistence;

public class BotsContext : DbContext
{
    public BotsContext(DbContextOptions<BotsContext> options) : base(options) { }

    public DbSet<Bot> Bots { get; set; }

    public DbSet<BotUpdate> BotUpdates { get; set; }

    public DbSet<BotFatherSession> BotFatherSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Id = Users.Id, задаётся вызывающим (не IDENTITY)
        modelBuilder.Entity<Bot>()
            .Property(b => b.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<Bot>()
            .HasIndex(b => b.OwnerUserId);

        // Не более одного бота на системную роль
        modelBuilder.Entity<Bot>()
            .HasIndex(b => b.SystemRole)
            .IsUnique()
            .HasFilter("\"SystemRole\" <> 0");

        modelBuilder.Entity<BotUpdate>()
            .HasOne(u => u.Bot)
            .WithMany()
            .HasForeignKey(u => u.BotId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BotUpdate>()
            .HasIndex(u => new { u.BotId, u.Id });

        modelBuilder.Entity<BotUpdate>()
            .Property(u => u.Payload)
            .HasColumnType("jsonb");

        modelBuilder.Entity<BotFatherSession>()
            .HasKey(s => s.UserId);

        modelBuilder.Entity<BotFatherSession>()
            .Property(s => s.UserId)
            .ValueGeneratedNever();

        base.OnModelCreating(modelBuilder);
    }
}
