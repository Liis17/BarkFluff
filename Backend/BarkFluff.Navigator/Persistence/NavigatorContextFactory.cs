using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Navigator.Persistence;

/// <summary>
/// Factory для создания DbContext в design-time (для миграций)
/// </summary>
public class NavigatorContextFactory : IDesignTimeDbContextFactory<NavigatorContext>
{
    public NavigatorContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NavigatorContext>();

        optionsBuilder.UseNpgsql("Host=localhost;Database=barkfluff_navigator;Username=postgres;Password=your_password");

        return new NavigatorContext(optionsBuilder.Options);
    }
}
