using Barkfluff.Developers.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Barkfluff.Developers.Persistence;

public class DevelopersContextFactory : IDesignTimeDbContextFactory<DevelopersContext>
{
    public DevelopersContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DevelopersContext>()
            .UseNpgsql("Host=localhost;Database=developers;Username=postgres;Password=postgres")
            .Options;

        return new DevelopersContext(options);
    }
}
