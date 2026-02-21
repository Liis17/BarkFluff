using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarkFluff.Files.Persistence;

/// <summary>
/// Используется только инструментами EF Core для создания миграций без запуска приложения.
/// </summary>
public class FilesContextFactory : IDesignTimeDbContextFactory<FilesContext>
{
    public FilesContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FilesContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=barkfluff_files;Username=postgres;Password=postgres");
        return new FilesContext(optionsBuilder.Options);
    }
}
