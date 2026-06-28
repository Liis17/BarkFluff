using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Calls.Persistence;

/// <summary>
/// Design-time фабрика для `dotnet ef migrations` (рантайм использует строку из Configuration).
/// </summary>
public class CallsContextFactory : IDesignTimeDbContextFactory<CallsContext>
{
    public CallsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CallsContext>()
            .UseNpgsql("Host=localhost;Database=calls_dev;Username=postgres;Password=postgres")
            .Options;

        return new CallsContext(options);
    }
}
