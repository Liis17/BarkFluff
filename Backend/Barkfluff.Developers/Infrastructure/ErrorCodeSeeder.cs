using System.Reflection;
using BarkFluff.Shared.Exceptions;
using Barkfluff.Developers.Domain;
using Barkfluff.Developers.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Barkfluff.Developers.Infrastructure;

public class ErrorCodeSeeder
{
    public async Task<int> SeedMissingAsync(DevelopersContext context, CancellationToken cancellationToken = default)
    {
        var entries = DiscoverEntries();

        if (context.Database.IsNpgsql())
        {
            var inserted = 0;
            foreach (var entry in entries)
            {
                inserted += await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ErrorCodes"
                        ("id", "code", "exception_name", "description", "domain")
                    VALUES
                        ({entry.Id}, {entry.Code}, {entry.ExceptionName}, {entry.Description}, {entry.Domain})
                    ON CONFLICT ("code") DO NOTHING
                    """, cancellationToken);
            }

            return inserted;
        }

        var existingCodes = await context.ErrorCodes
            .AsNoTracking()
            .Select(entry => entry.Code)
            .ToHashSetAsync(cancellationToken);
        var missingEntries = entries
            .Where(entry => !existingCodes.Contains(entry.Code))
            .ToList();

        if (missingEntries.Count == 0)
            return 0;

        context.ErrorCodes.AddRange(missingEntries);
        return await context.SaveChangesAsync(cancellationToken);
    }

    internal static IReadOnlyList<ErrorCodeEntry> DiscoverEntries()
    {
        var exceptionTypes = typeof(BaseGrpcException).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(BaseGrpcException)) && !type.IsAbstract)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        var entries = new List<ErrorCodeEntry>(exceptionTypes.Count);
        var codes = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in exceptionTypes)
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Error code invariant failed: exception type '{type.FullName}' must have a public parameterless constructor.");
            }

            BaseGrpcException instance;
            try
            {
                instance = (BaseGrpcException)Activator.CreateInstance(type)!;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Error code invariant failed: exception type '{type.FullName}' could not be instantiated.",
                    exception);
            }

            if (string.IsNullOrWhiteSpace(instance.ErrorCode))
            {
                throw new InvalidOperationException(
                    $"Error code invariant failed: exception type '{type.FullName}' has an empty error code.");
            }

            if (codes.TryGetValue(instance.ErrorCode, out var duplicateType))
            {
                throw new InvalidOperationException(
                    $"Error code invariant failed: code '{instance.ErrorCode}' is declared by both '{duplicateType.FullName}' and '{type.FullName}'.");
            }

            codes.Add(instance.ErrorCode, type);

            var ns = type.Namespace ?? string.Empty;
            var domain = ns.Split('.').LastOrDefault() ?? "Common";

            entries.Add(new ErrorCodeEntry
            {
                Id = Guid.NewGuid(),
                Code = instance.ErrorCode,
                ExceptionName = type.Name,
                Description = instance.ErrorMessage,
                Domain = domain
            });
        }

        return entries;
    }
}
