using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.ClientStorage.Persistence;

public class ClientStorageContextFactory : IDesignTimeDbContextFactory<ClientStorageContext>
{
    public ClientStorageContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClientStorageContext>();
        optionsBuilder.UseSqlite("Data Source=clientstorage.db");
        return new ClientStorageContext(optionsBuilder.Options);
    }
}
