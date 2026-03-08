using BarkFluff.Onliner.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Onliner.Persistence.Contexts;

public class OnlineStatusContext : DbContext
{
    public DbSet<UserOnlineStatus> UsersOnlineStatuses { get; set; }

    public OnlineStatusContext(DbContextOptions<OnlineStatusContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserOnlineStatus>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.LastSeen).IsRequired();
        });
    }
}