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
    public DbSet<AuthenticationChallenge> AuthenticationChallenges { get; set; }
    public DbSet<RecoveryCode> RecoveryCodes { get; set; }
    public DbSet<TelegramPollingState> TelegramPollingStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.Value)
            .IsUnique();

        modelBuilder.Entity<AuthUserProperty>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<AuthUserProperty>().HasIndex(x => x.TelegramId).IsUnique();
        modelBuilder.Entity<AuthenticationChallenge>().HasIndex(x => x.TelegramTokenHash).IsUnique();
        modelBuilder.Entity<AuthenticationChallenge>().HasIndex(x => x.AttemptId).IsUnique();
        modelBuilder.Entity<AuthenticationChallenge>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<RecoveryCode>().HasIndex(x => new { x.UserId, x.Hash }).IsUnique();
    }
}
