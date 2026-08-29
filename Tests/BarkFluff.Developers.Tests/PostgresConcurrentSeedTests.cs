using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Contexts;
using Barkfluff.Developers.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Barkfluff.Developers.Tests;

public class PostgresConcurrentSeedTests
{
    [PostgresFact]
    public async Task Two_startup_initializers_do_not_create_duplicate_defaults()
    {
        var connectionString = Environment.GetEnvironmentVariable("DEVELOPERS_TEST_POSTGRES")!;

        await using var first = CreateProvider(connectionString);
        await using var second = CreateProvider(connectionString);

        var firstInitializer = first.GetRequiredService<DevelopersStartupInitializer>();
        var secondInitializer = second.GetRequiredService<DevelopersStartupInitializer>();

        await Task.WhenAll(
            firstInitializer.InitializeAsync(),
            secondInitializer.InitializeAsync());

        await using var verification = CreateProvider(connectionString);
        var context = verification.GetRequiredService<DevelopersContext>();

        (await context.DocumentationSections.CountAsync()).Should()
            .Be((await context.DocumentationSections.Select(section => section.Key).Distinct().CountAsync()));
        (await context.ProtoMetadata.CountAsync()).Should()
            .Be((await context.ProtoMetadata.Select(item => item.FileName).Distinct().CountAsync()));
        (await context.ErrorCodes.CountAsync()).Should()
            .Be((await context.ErrorCodes.Select(entry => entry.Code).Distinct().CountAsync()));
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevelopersContext>(options => options.UseNpgsql(connectionString));
        services.AddTransient<DocumentationStorage>();
        services.AddTransient<ProtoMetadataStorage>();
        services.AddSingleton<ErrorCodeSeeder>();
        services.AddSingleton<IPublishedProtoCatalog>(new TestInfrastructure.TestPublishedProtoCatalog());
        services.AddSingleton<DevelopersStartupInitializer>();
        return services.BuildServiceProvider();
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEVELOPERS_TEST_POSTGRES")))
                Skip = "DEVELOPERS_TEST_POSTGRES is not configured.";
        }
    }
}
