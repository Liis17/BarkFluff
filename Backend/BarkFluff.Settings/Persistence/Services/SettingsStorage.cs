extern alias GrpcServer;

using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using MetricsCollector = GrpcServer::BarkFluff.GrpcServer.Metrics.MetricsCollector;

namespace BarkFluff.Settings.Persistence.Services;

public sealed record StoredSetting(
    ServiceId ServiceId,
    string Section,
    string Key,
    string StorageKey,
    string Value,
    DateTime EditedAt,
    string EditedBy,
    string EditedFrom,
    bool IsSensitive);

public sealed class SettingsStorage
{
    private readonly SettingsContext _context;
    private readonly MetricsCollector _metrics;

    public SettingsStorage(SettingsContext context, MetricsCollector metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<IReadOnlyList<StoredSetting>> GetConfigurationAsync(
        ServiceId serviceId,
        CancellationToken cancellationToken = default)
    {
        if (!SettingsScopes.TryGet(serviceId, out _))
            throw new ArgumentOutOfRangeException(nameof(serviceId), serviceId, "Unknown service id.");

        var selected = new Dictionary<string, StoredSetting>(StringComparer.Ordinal);
        foreach (var scope in serviceId == ServiceId.Unknown
                     ? new[] { SettingsScopes.Get(ServiceId.Unknown) }
                     : new[] { SettingsScopes.Get(ServiceId.Unknown), SettingsScopes.Get(serviceId) })
        {
            foreach (var setting in await ReadScopeAsync(scope, cancellationToken))
                selected[setting.StorageKey] = setting;
        }

        return selected.Values.OrderBy(item => item.StorageKey, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<StoredSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<StoredSetting>();
        foreach (var scope in SettingsScopes.All)
            result.AddRange(await ReadScopeAsync(scope, cancellationToken));
        return result.OrderBy(item => item.ServiceId).ThenBy(item => item.StorageKey, StringComparer.Ordinal).ToArray();
    }

    public async Task UpdateAsync(
        string section,
        string key,
        string value,
        ServiceId serviceId,
        string editedBy,
        string editedFrom,
        CancellationToken cancellationToken = default)
    {
        var catalogEntry = SettingsCatalog.Resolve(serviceId, section, key);
        var scope = SettingsScopes.Get(serviceId);
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            var row = await GetLockedRowAsync(scope, catalogEntry.StorageKey, cancellationToken)
                ?? throw new InvalidOperationException($"Catalog row {scope.TableName}.{catalogEntry.StorageKey} is missing.");
            var changedAt = DateTime.UtcNow;
            var previous = row.Value;
            row.Value = value;
            row.EditedBy = NormalizeActor(editedBy);
            row.EditedAt = changedAt;
            _context.SettingsHistory.Add(new SettingRevision
            {
                SettingsTable = scope.TableName,
                Key = catalogEntry.StorageKey,
                PreviousValue = previous,
                NewValue = value,
                ChangedAt = changedAt,
                ChangedBy = NormalizeActor(editedBy),
                ChangedFrom = editedFrom ?? string.Empty,
                ChangeKind = "Update"
            });
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        });
        _metrics.Increment("configurations_db_writes");
    }

    public async Task<IReadOnlyList<SettingRevision>> GetHistoryAsync(
        string section,
        string key,
        ServiceId serviceId,
        int count,
        CancellationToken cancellationToken = default)
    {
        var entry = SettingsCatalog.Resolve(serviceId, section, key);
        var scope = SettingsScopes.Get(serviceId);
        return await _context.SettingsHistory.AsNoTracking()
            .Where(revision => revision.SettingsTable == scope.TableName && revision.Key == entry.StorageKey)
            .OrderByDescending(revision => revision.ChangedAt)
            .ThenByDescending(revision => revision.Id)
            .Take(Math.Clamp(count, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task RollbackAsync(
        long revisionId,
        string editedBy,
        string editedFrom,
        CancellationToken cancellationToken = default)
    {
        var source = await _context.SettingsHistory.AsNoTracking()
            .SingleOrDefaultAsync(revision => revision.Id == revisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Revision {revisionId} was not found.");
        if (!SettingsScopes.TryGet(source.SettingsTable, out var scope))
            throw new InvalidOperationException($"Revision {revisionId} references unknown table {source.SettingsTable}.");
        SettingsCatalog.Resolve(scope.ServiceId, source.Key);

        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            var row = await GetLockedRowAsync(scope, source.Key, cancellationToken)
                ?? throw new InvalidOperationException($"Settings row {scope.TableName}.{source.Key} was not found.");
            var changedAt = DateTime.UtcNow;
            var currentValue = row.Value;
            row.Value = source.PreviousValue;
            row.EditedBy = NormalizeActor(editedBy);
            row.EditedAt = changedAt;
            _context.SettingsHistory.Add(new SettingRevision
            {
                SettingsTable = scope.TableName,
                Key = source.Key,
                PreviousValue = currentValue,
                NewValue = source.PreviousValue,
                ChangedAt = changedAt,
                ChangedBy = NormalizeActor(editedBy),
                ChangedFrom = editedFrom ?? string.Empty,
                ChangeKind = "Rollback",
                SourceRevisionId = source.Id
            });
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        });
        _metrics.Increment("configurations_db_writes");
    }

    public async Task<IReadOnlyList<string>> GetReservedNamesAsync(CancellationToken cancellationToken = default)
    {
        var names = await _context.ReservedNames.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
        _metrics.Set("reserved_names_count", names.Count);
        return names;
    }

    public async Task AddReservedNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        if (await _context.ReservedNames.AnyAsync(item => item.Name == normalized, cancellationToken))
            throw new InvalidOperationException($"Name '{normalized}' is already reserved.");
        _context.ReservedNames.Add(new ReservedName { Name = normalized });
        await _context.SaveChangesAsync(cancellationToken);
        await UpdateReservedNamesGaugeAsync(cancellationToken);
    }

    public async Task UpdateReservedNameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var oldNormalized = NormalizeName(oldName);
        var newNormalized = NormalizeName(newName);
        var existing = await _context.ReservedNames.SingleOrDefaultAsync(item => item.Name == oldNormalized, cancellationToken)
            ?? throw new InvalidOperationException($"Name '{oldNormalized}' was not found.");
        if (oldNormalized != newNormalized && await _context.ReservedNames.AnyAsync(item => item.Name == newNormalized, cancellationToken))
            throw new InvalidOperationException($"Name '{newNormalized}' is already reserved.");
        _context.ReservedNames.Remove(existing);
        _context.ReservedNames.Add(new ReservedName { Name = newNormalized });
        await _context.SaveChangesAsync(cancellationToken);
        await UpdateReservedNamesGaugeAsync(cancellationToken);
    }

    public async Task DeleteReservedNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        var existing = await _context.ReservedNames.SingleOrDefaultAsync(item => item.Name == normalized, cancellationToken)
            ?? throw new InvalidOperationException($"Name '{normalized}' was not found.");
        _context.ReservedNames.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
        await UpdateReservedNamesGaugeAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<StoredSetting>> ReadScopeAsync(SettingsScope scope, CancellationToken cancellationToken)
    {
        var rows = await _context.Settings(scope).AsNoTracking().ToListAsync(cancellationToken);
        var revisions = await _context.SettingsHistory.AsNoTracking()
            .Where(revision => revision.SettingsTable == scope.TableName)
            .OrderByDescending(revision => revision.ChangedAt)
            .ThenByDescending(revision => revision.Id)
            .ToListAsync(cancellationToken);
        var latest = revisions.GroupBy(revision => revision.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return rows.Select(row =>
        {
            var entry = SettingsCatalog.Resolve(scope.ServiceId, row.Key);
            return new StoredSetting(
                scope.ServiceId,
                entry.Section,
                entry.Key,
                row.Key,
                row.Value,
                DateTime.SpecifyKind(row.EditedAt, DateTimeKind.Utc),
                row.EditedBy,
                latest.TryGetValue(row.Key, out var revision) ? revision.ChangedFrom : string.Empty,
                entry.IsSensitive);
        }).ToArray();
    }

    private async Task<SettingRow?> GetLockedRowAsync(SettingsScope scope, string key, CancellationToken cancellationToken)
    {
        var settings = _context.Settings(scope);
        if (!string.Equals(
                _context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            return await settings.SingleOrDefaultAsync(row => row.Key == key, cancellationToken);

        // The table name comes only from the closed SettingsScopes catalog; the key remains parameterized.
#pragma warning disable EF1002
        return await settings
            .FromSqlRaw($"SELECT * FROM \"{scope.TableName}\" WHERE \"Key\" = {{0}} FOR UPDATE", key)
            .SingleOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private async Task UpdateReservedNamesGaugeAsync(CancellationToken cancellationToken) =>
        _metrics.Set("reserved_names_count", await _context.ReservedNames.CountAsync(cancellationToken));

    private static string NormalizeActor(string? actor) => string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0) throw new ArgumentException("Name cannot be empty.", nameof(name));
        return normalized;
    }
}
