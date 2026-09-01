using Barkfluff.Developers.Domain;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Contexts;
using Barkfluff.Developers.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Barkfluff.Developers.Tests;

internal static class TestInfrastructure
{
    public static DevelopersContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<DevelopersContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        var context = new DevelopersContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static ServiceProvider CreateInitializerProvider(
        string databaseName,
        IPublishedProtoCatalog? catalog = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevelopersContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddTransient<DocumentationStorage>();
        services.AddTransient<ProtoMetadataStorage>();
        services.AddSingleton<ErrorCodeSeeder>();
        services.AddSingleton<IPublishedProtoCatalog>(catalog ?? new TestPublishedProtoCatalog());
        services.AddSingleton<DevelopersStartupInitializer>();
        return services.BuildServiceProvider();
    }

    internal sealed class TestPublishedProtoCatalog : IPublishedProtoCatalog
    {
        private readonly HashSet<string> _missingFiles;

        public TestPublishedProtoCatalog(IEnumerable<string>? missingFiles = null)
        {
            _missingFiles = (missingFiles ?? [])
                .ToHashSet(StringComparer.Ordinal);
        }

        public IReadOnlyList<string> PublishedFileNames => PublishedProtoManifest.FileNames;

        public bool IsPublished(string fileName)
        {
            return PublishedProtoManifest.Contains(fileName);
        }

        public string? GetContent(string fileName)
        {
            return IsPublished(fileName) && !_missingFiles.Contains(fileName)
                ? "syntax = \"proto3\";"
                : null;
        }

        public IReadOnlyList<string> GetMissingFiles()
        {
            return PublishedFileNames
                .Where(_missingFiles.Contains)
                .ToArray();
        }
    }

    internal static DocumentationSection Documentation(
        string key,
        string content = "{}")
    {
        return new DocumentationSection
        {
            Id = Guid.NewGuid(),
            Key = key,
            Title = key,
            Type = "test",
            Order = 1,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    internal static ProtoMetadata ProtoMetadata(
        string fileName,
        int order = 1)
    {
        return new ProtoMetadata
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            DisplayName = fileName,
            Slug = fileName.Replace(".proto", string.Empty, StringComparison.Ordinal),
            Order = order,
            RpcDescriptions = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
