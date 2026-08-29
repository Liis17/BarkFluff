using System.Text.Json;
using Barkfluff.Developers.Persistence.Contexts;
using Barkfluff.Developers.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Barkfluff.Developers.Infrastructure;

internal sealed class DevelopersStartupInitializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublishedProtoCatalog _catalog;
    private readonly ILogger<DevelopersStartupInitializer> _logger;

    public DevelopersStartupInitializer(
        IServiceScopeFactory scopeFactory,
        IPublishedProtoCatalog catalog,
        ILogger<DevelopersStartupInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DevelopersContext>();

        _logger.LogInformation("Developers startup initialization started");

        if (context.Database.IsRelational())
            await context.Database.MigrateAsync(cancellationToken);

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var documentationInserted = await scope.ServiceProvider
                .GetRequiredService<DocumentationStorage>()
                .SeedMissingAsync(cancellationToken);
            var protoMetadataInserted = await scope.ServiceProvider
                .GetRequiredService<ProtoMetadataStorage>()
                .SeedMissingAsync(cancellationToken);
            var errorCodesInserted = await scope.ServiceProvider
                .GetRequiredService<ErrorCodeSeeder>()
                .SeedMissingAsync(context, cancellationToken);

            await ValidateInvariantsAsync(context, cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Developers startup initialization completed; inserted documentation sections: {DocumentationSectionsInserted}, proto metadata: {ProtoMetadataInserted}, error codes: {ErrorCodesInserted}",
                documentationInserted,
                protoMetadataInserted,
                errorCodesInserted);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);

            throw;
        }
    }

    private async Task ValidateInvariantsAsync(DevelopersContext context, CancellationToken cancellationToken)
    {
        var missingProtoFiles = _catalog.GetMissingFiles();
        if (missingProtoFiles.Count > 0)
        {
            throw new InvalidOperationException(
                $"Developers startup invariant failed: published proto files are missing from the application output: {string.Join(", ", missingProtoFiles)}.");
        }

        var publishedFileNames = _catalog.PublishedFileNames.ToHashSet(StringComparer.Ordinal);
        var metadata = await context.ProtoMetadata
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var missingMetadata = publishedFileNames
            .Where(fileName => metadata.All(item => !string.Equals(item.FileName, fileName, StringComparison.Ordinal)))
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        if (missingMetadata.Length > 0)
        {
            throw new InvalidOperationException(
                $"Developers startup invariant failed: published proto metadata is missing for: {string.Join(", ", missingMetadata)}.");
        }

        var metadataWithoutFiles = metadata
            .Where(item => _catalog.IsPublished(item.FileName) && _catalog.GetContent(item.FileName) is null)
            .Select(item => item.FileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        if (metadataWithoutFiles.Length > 0)
        {
            throw new InvalidOperationException(
                $"Developers startup invariant failed: proto metadata has no physical published file for: {string.Join(", ", metadataWithoutFiles)}.");
        }

        var documentation = await context.DocumentationSections
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        ValidateJsonColumns(
            documentation.Select(section => (section.Key, section.Content)),
            "documentation section");
        ValidateJsonColumns(
            metadata.Select(item => (item.FileName, item.RpcDescriptions)),
            "proto metadata");

        var duplicateErrorCodes = await context.ErrorCodes
            .AsNoTracking()
            .GroupBy(entry => entry.Code)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);
        duplicateErrorCodes.Sort(StringComparer.Ordinal);

        if (duplicateErrorCodes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Developers startup invariant failed: duplicate error codes exist in the database: {string.Join(", ", duplicateErrorCodes)}.");
        }
    }

    private static void ValidateJsonColumns(
        IEnumerable<(string Key, string Content)> values,
        string valueDescription)
    {
        foreach (var (key, content) in values)
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("The JSON root must be an object.");
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Developers startup invariant failed: {valueDescription} '{key}' contains invalid JSON.",
                    exception);
            }
        }
    }
}
