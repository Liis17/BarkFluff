using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Messages.Persistence;

public class MessagesContextFactory : IDesignTimeDbContextFactory<MessagesContext>
{
    public MessagesContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MessagesContext>()
            .UseNpgsql("Host=localhost;Database=messages_dev;Username=postgres;Password=postgres")
            .Options;

        return new MessagesContext(options);
    }
}
