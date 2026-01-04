using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Onliner.Persistence.Contexts;

/// <summary>
/// Factory для создания DbContext в design-time (для миграций)
/// </summary>
public class OnlineStatusContextFactory : IDesignTimeDbContextFactory<OnlineStatusContext>
{
    public OnlineStatusContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OnlineStatusContext>();

        // Используйте вашу локальную connection string для создания миграций
        // Можно взять из appsettings.Development.json или задать здесь
        optionsBuilder.UseNpgsql("Host=localhost;Database=barkfluff_onliner;Username=postgres;Password=your_password");

        return new OnlineStatusContext(optionsBuilder.Options);
    }
}
