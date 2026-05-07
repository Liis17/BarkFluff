using BarkFluff.Identity.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Contexts;

public class IdentityContext : DbContext
{
    public IdentityContext(DbContextOptions<IdentityContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<ConfirmationCode> ConfirmationCodes { get; set; }

    public DbSet<AuthUserProperty> AuthUserProperties { get; set; }

    public DbSet<ResetPassword> ResetPasswords { get; set; }

    public DbSet<UserPassword> UserPasswords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.Value)
            .IsUnique();
    }
}
