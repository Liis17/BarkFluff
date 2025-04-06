using BarkFluff.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Contexts;

public class IdentityContext : DbContext
{
    public DbSet<RefreshToken> RefreshTokens { get; set; }
}