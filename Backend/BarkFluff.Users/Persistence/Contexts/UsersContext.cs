using BarkFluff.Users.Domain;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Contexts;

public class UsersContext : DbContext
{
    public UsersContext(DbContextOptions<UsersContext> options)  : base(options) { }

    public DbSet<User> Users { get; set; }
    
    public DbSet<UserContact> UserContacts { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasOne(u => u.Contact)
            .WithOne(p => p.User)
            .HasForeignKey<UserContact>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        base.OnModelCreating(modelBuilder);
    
        // Настройка функций полнотекстового поиска
        modelBuilder.ConfigureFullTextSearch();
    }
}