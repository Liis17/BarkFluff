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

    public DbSet<Privacy> Privacies { get; set; }

    public DbSet<UserPersonalization> UserPersonalizations { get; set; }

    public DbSet<ChatFolder> ChatFolders { get; set; }

    public DbSet<ChatMute> ChatMutes { get; set; }

    public DbSet<DevicePrekeyBundle> DevicePrekeyBundles { get; set; }

    public DbSet<OneTimePrekey> OneTimePrekeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasOne(u => u.Contact)
            .WithOne(p => p.User)
            .HasForeignKey<UserContact>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Уникальный индекс для глобального идентификатора пользователя (федерация, Фаза 0)
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Uuid)
            .IsUnique();

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

        // Настройка связей для Privacy (1:1 с User, уникальный индекс UserId)
        modelBuilder.Entity<Privacy>()
            .HasOne(p => p.User)
            .WithOne()
            .HasForeignKey<Privacy>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Privacy>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        // Настройка связей для UserPersonalization (1:1 с User, уникальный индекс UserId)
        modelBuilder.Entity<UserPersonalization>()
            .HasOne(p => p.User)
            .WithOne()
            .HasForeignKey<UserPersonalization>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserPersonalization>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        // Настройка связей для ChatFolder (1:Many с User; уникальный индекс по публичному FolderId)
        modelBuilder.Entity<ChatFolder>()
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatFolder>()
            .HasIndex(f => f.OwnerUserId);

        modelBuilder.Entity<ChatFolder>()
            .HasIndex(f => f.FolderId)
            .IsUnique();

        // Настройка связей для ChatMute (1:Many с User; уникальный индекс (UserId, ChatId))
        modelBuilder.Entity<ChatMute>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatMute>()
            .HasIndex(m => new { m.UserId, m.ChatId })
            .IsUnique();

        // DevicePrekeyBundle (1:1 с UserDevice; PK = DeviceId)
        modelBuilder.Entity<DevicePrekeyBundle>()
            .HasOne(b => b.Device)
            .WithOne()
            .HasForeignKey<DevicePrekeyBundle>(b => b.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // OneTimePrekey (Many:1 с UserDevice; уникальный (DeviceId, PrekeyId))
        modelBuilder.Entity<OneTimePrekey>()
            .HasOne(p => p.Device)
            .WithMany()
            .HasForeignKey(p => p.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OneTimePrekey>()
            .HasIndex(p => new { p.DeviceId, p.PrekeyId })
            .IsUnique();

        modelBuilder.Entity<OneTimePrekey>()
            .HasIndex(p => p.DeviceId);

        base.OnModelCreating(modelBuilder);

        // Настройка функций полнотекстового поиска
        modelBuilder.ConfigureFullTextSearch();
    }
}