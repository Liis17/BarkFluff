using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Users.Persistence.Contexts;

public class UsersContextFactory : IDesignTimeDbContextFactory<UsersContext>
{
    public UsersContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<UsersContext>();

        // Используем тестовую строку подключения для миграций
        optionsBuilder.UseNpgsql("Host=localhost;Database=barkfluff_users;Username=postgres;Password=password");

        return new UsersContext(optionsBuilder.Options);
    }
}