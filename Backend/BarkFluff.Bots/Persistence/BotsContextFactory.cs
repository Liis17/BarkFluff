using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Bots.Persistence;

/// <summary>
/// Design-time фабрика для `dotnet ef migrations` (рантайм использует строку из Configuration).
/// </summary>
public class BotsContextFactory : IDesignTimeDbContextFactory<BotsContext>
{
    public BotsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BotsContext>()
            .UseNpgsql("Host=localhost;Database=bots_dev;Username=postgres;Password=postgres")
            .Options;

        return new BotsContext(options);
    }
}
