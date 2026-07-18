using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Federation.Persistence.Contexts;

/// <summary>
/// Factory для создания DbContext в design-time (для миграций)
/// </summary>
public class FederationContextFactory : IDesignTimeDbContextFactory<FederationContext>
{
    public FederationContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FederationContext>();

        optionsBuilder.UseNpgsql("Host=localhost;Database=barkfluff_federation;Username=postgres;Password=your_password");

        return new FederationContext(optionsBuilder.Options);
    }
}
