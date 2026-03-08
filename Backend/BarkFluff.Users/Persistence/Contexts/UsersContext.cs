using BarkFluff.Users.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Contexts;

public class UsersContext : DbContext
{
    public UsersContext(DbContextOptions<UsersContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }

    public DbSet<UserContact> UserContacts { get; set; }

    public DbSet<Badge> Badges { get; set; }

    public DbSet<UserBadge> UserBadges { get; set; }

    public DbSet<UserDevice> UserDevices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasOne(u => u.Contact)
            .WithOne(p => p.User)
            .HasForeignKey<UserContact>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Настройка связей для UserBadge
        modelBuilder.Entity<UserBadge>()
            .HasOne(ub => ub.User)
            .WithMany()
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserBadge>()
            .HasOne(ub => ub.Badge)
            .WithMany(b => b.UserBadges)
            .HasForeignKey(ub => ub.BadgeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Уникальный индекс для предотвращения дублирования назначения одного баджа пользователю
        modelBuilder.Entity<UserBadge>()
            .HasIndex(ub => new { ub.UserId, ub.BadgeId })
            .IsUnique();

        // Настройка связей для UserDevice
        modelBuilder.Entity<UserDevice>()
            .HasOne(ud => ud.User)
            .WithMany()
            .HasForeignKey(ud => ud.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);

        // Настройка функций полнотекстового поиска
        modelBuilder.ConfigureFullTextSearch();
    }
}