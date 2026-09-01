extern alias GrpcServer;

using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using MetricsCollector = GrpcServer::BarkFluff.GrpcServer.Metrics.MetricsCollector;

namespace BarkFluff.Settings.Infrastructure;

public sealed record SetupFieldSnapshot(
    string Id,
    ServiceId ServiceId,
    string Section,
    string Key,
    string StorageKey,
    SetupFieldMetadata Metadata,
    bool IsSensitive,
    bool Required,
    bool Applicable,
    bool Configured,
    string Value,
    string? Error);

public sealed record SetupGroupSnapshot(
    SetupGroupMetadata Metadata,
    bool Applicable,
    bool Complete,
    IReadOnlyList<SetupFieldSnapshot> Fields);

public sealed record SetupSnapshot(
    bool Complete,
    bool Locked,
    string CatalogFingerprint,
    DateTime? CompletedAtUtc,
    IReadOnlyList<SetupGroupSnapshot> Groups);

public sealed class SetupLockedException : InvalidOperationException
{
    public SetupLockedException() : base("Initial setup is already complete.") { }
}

public sealed class SetupIncompleteException : InvalidOperationException
{
    public SetupIncompleteException(IEnumerable<string> fields)
        : base($"Required setup fields are incomplete: {string.Join(", ", fields)}") { }
}

public sealed class SetupFieldValidationException : ArgumentException
{
    public SetupFieldValidationException(string fieldId, string error)
        : base(error, fieldId) { }
}

public sealed class SettingsSetupCoordinator
{
    private const int CompletionStateId = 1;
    private const long SetupAdvisoryLockKey = 0x42465345545550;
    private static readonly SemaphoreSlim SetupGate = new(1, 1);

    private readonly SettingsContext _context;
    private readonly MetricsCollector? _metrics;

    public SettingsSetupCoordinator(SettingsContext context, MetricsCollector? metrics = null)
    {
        _context = context;
        _metrics = metrics;
    }

    public Task<SetupSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        GetSnapshotCoreAsync(cancellationToken);

    public async Task<SetupSnapshot> SaveGroupAsync(
        string groupId,
        IReadOnlyDictionary<string, string?> values,
        string editedBy,
        string editedFrom,
        CancellationToken cancellationToken = default)
    {
        await SetupGate.WaitAsync(cancellationToken);
        try
        {
            return await SaveGroupCoreAsync(groupId, values, editedBy, editedFrom, cancellationToken);
        }
        finally
        {
            SetupGate.Release();
        }
    }

    public async Task<SetupSnapshot> CompleteAsync(
        string completedBy,
        string completedFrom,
        CancellationToken cancellationToken = default)
    {
        await SetupGate.WaitAsync(cancellationToken);
        try
        {
            return await CompleteCoreAsync(completedBy, completedFrom, cancellationToken);
        }
        finally
        {
            SetupGate.Release();
        }
    }

    private async Task<SetupSnapshot> SaveGroupCoreAsync(
        string groupId,
        IReadOnlyDictionary<string, string?> values,
        string editedBy,
        string editedFrom,
        CancellationToken cancellationToken)
    {
        var changed = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            await AcquireDatabaseLockAsync(cancellationToken);

            var before = await GetSnapshotCoreAsync(cancellationToken);
            if (before.Locked)
                throw new SetupLockedException();

            var group = SettingsSetupMetadata.Groups.SingleOrDefault(item => item.Id == groupId)
                ?? throw new KeyNotFoundException($"Unknown setup group '{groupId}'.");
            var entries = SettingsCatalog.All
                .Where(entry => entry.Setup?.GroupId == group.Id)
                .OrderBy(entry => entry.Setup!.Order)
                .ToArray();
            var knownIds = entries.ToDictionary(SettingsSetupMetadata.GetFieldId, StringComparer.Ordinal);
            var unknown = values.Keys.Where(key => !knownIds.ContainsKey(key)).Order(StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
                throw new ArgumentException($"Unknown fields in setup group '{groupId}': {string.Join(", ", unknown)}.");

            var current = await ReadSetupValuesAsync(cancellationToken);
            var federationEnabled = IsFederationEnabled(current);
            if (group.Id == "federation"
                && values.TryGetValue(SettingsSetupMetadata.GetFieldId(knownIds.Values.Single(entry => entry.Key == "Enabled")), out var enabledValue)
                && bool.TryParse(enabledValue, out var requestedFederationEnabled))
                federationEnabled = requestedFederationEnabled;

            var changed = 0;
            foreach (var (fieldId, rawValue) in values)
            {
                var entry = knownIds[fieldId];
                if (entry.Setup!.Requirement == SetupRequirement.FederationEnabled && !federationEnabled
                    && entry.StorageKey != "Federation:Enabled")
                    continue;

                var scope = SettingsScopes.Get(entry.ServiceId);
                var row = await GetLockedRowAsync(scope, entry.StorageKey, cancellationToken)
                    ?? throw new InvalidOperationException($"Catalog row {scope.TableName}.{entry.StorageKey} is missing.");
                var currentValue = row.Value;
                var result = SettingsSetupValidation.Validate(entry, rawValue, currentValue);
                if (!result.IsValid)
                    throw new SetupFieldValidationException(fieldId, result.Error ?? "Invalid setup value.");

                if (string.Equals(result.Value, currentValue, StringComparison.Ordinal))
                    continue;

                var changedAt = DateTime.UtcNow;
                row.Value = result.Value;
                row.EditedBy = NormalizeActor(editedBy);
                row.EditedAt = changedAt;
                _context.SettingsHistory.Add(new SettingRevision
                {
                    SettingsTable = scope.TableName,
                    Key = entry.StorageKey,
                    PreviousValue = currentValue,
                    NewValue = result.Value,
                    ChangedAt = changedAt,
                    ChangedBy = NormalizeActor(editedBy),
                    ChangedFrom = editedFrom ?? string.Empty,
                    ChangeKind = "Setup"
                });
                changed++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return changed;
        });
        _metrics?.Add("setup_group_fields_changed", changed);
        return await GetSnapshotCoreAsync(cancellationToken);
    }

    private async Task<SetupSnapshot> CompleteCoreAsync(
        string completedBy,
        string completedFrom,
        CancellationToken cancellationToken)
    {
        var completed = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginTransactionAsync(cancellationToken);
            await AcquireDatabaseLockAsync(cancellationToken);

            var snapshot = await GetSnapshotCoreAsync(cancellationToken);
            if (snapshot.Locked)
                return false;

            if (!snapshot.Complete)
            {
                var missing = snapshot.Groups
                    .SelectMany(group => group.Fields)
                    .Where(field => field.Applicable && field.Required && (!field.Configured || field.Error is not null))
                    .Select(field => field.Id)
                    .ToArray();
                throw new SetupIncompleteException(missing);
            }

            var state = await _context.SetupStates.SingleOrDefaultAsync(state => state.Id == CompletionStateId, cancellationToken);
            var now = DateTime.UtcNow;
            if (state is null)
            {
                state = new SetupState { Id = CompletionStateId };
                _context.SetupStates.Add(state);
            }

            state.CatalogFingerprint = snapshot.CatalogFingerprint;
            state.CompletedAtUtc = now;
            state.CompletedBy = NormalizeActor(completedBy);
            state.CompletedFrom = completedFrom ?? string.Empty;
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return true;
        });
        if (completed)
            _metrics?.Increment("setup_completions");
        return await GetSnapshotCoreAsync(cancellationToken);
    }

    private async Task<SetupSnapshot> GetSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        var values = await ReadSetupValuesAsync(cancellationToken);
        var snapshot = BuildSnapshot(values);
        var completion = await _context.SetupStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.Id == CompletionStateId, cancellationToken);

        return snapshot with
        {
            // Completion is intentionally permanent. The fingerprint is retained for
            // audit/diagnostics; adding a catalog field must not silently reopen setup.
            Locked = completion is not null,
            CompletedAtUtc = completion?.CompletedAtUtc
        };
    }

    private SetupSnapshot BuildSnapshot(IReadOnlyDictionary<(ServiceId ServiceId, string StorageKey), string> values)
    {
        var federationEnabled = IsFederationEnabled(values);
        var groups = SettingsSetupMetadata.Groups
            .OrderBy(group => group.Order)
            .Select(group =>
            {
                var applicable = group.Id != "federation" || federationEnabled;
                var fields = SettingsCatalog.All
                    .Where(entry => entry.Setup?.GroupId == group.Id)
                    .OrderBy(entry => entry.Setup!.Order)
                    .Select(entry => BuildFieldSnapshot(entry, values, federationEnabled, applicable))
                    .ToArray();
                var complete = !applicable || fields.All(field =>
                    !field.Applicable || (field.Error is null && (!field.Required || field.Configured)));
                return new SetupGroupSnapshot(group, applicable, complete, fields);
            })
            .ToArray();
        var completeOverall = groups.All(group => !group.Applicable || group.Complete);
        return new SetupSnapshot(
            completeOverall,
            false,
            SettingsSetupMetadata.ComputeFingerprint(SettingsCatalog.All),
            null,
            groups);
    }

    private static SetupFieldSnapshot BuildFieldSnapshot(
        SettingsCatalogEntry entry,
        IReadOnlyDictionary<(ServiceId ServiceId, string StorageKey), string> values,
        bool federationEnabled,
        bool groupApplicable)
    {
        var metadata = entry.Setup!;
        var value = values.GetValueOrDefault((entry.ServiceId, entry.StorageKey), string.Empty);
        var applicable = (metadata.Requirement is SetupRequirement.None || groupApplicable)
            && SettingsSetupMetadata.IsApplicable(metadata.Requirement, federationEnabled);
        var required = applicable && metadata.Requirement is not SetupRequirement.None;
        var validation = string.IsNullOrEmpty(value) && !required
            ? SetupValidationResult.Success(value)
            : SettingsSetupValidation.Validate(entry, value, value);
        var configured = !string.IsNullOrWhiteSpace(value);
        return new SetupFieldSnapshot(
            SettingsSetupMetadata.GetFieldId(entry),
            entry.ServiceId,
            entry.Section,
            entry.Key,
            entry.StorageKey,
            metadata,
            entry.IsSensitive,
            required,
            applicable,
            configured,
            entry.IsSensitive ? string.Empty : value,
            !applicable || validation.IsValid ? null : validation.Error);
    }

    private async Task<Dictionary<(ServiceId ServiceId, string StorageKey), string>> ReadSetupValuesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<(ServiceId, string), string>();
        foreach (var group in SettingsCatalog.All.Where(entry => entry.Setup is not null).GroupBy(entry => entry.ServiceId))
        {
            var scope = SettingsScopes.Get(group.Key);
            var keys = group.Select(entry => entry.StorageKey).ToArray();
            var rows = await _context.Settings(scope)
                .AsNoTracking()
                .Where(row => keys.Contains(row.Key))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                result[(group.Key, row.Key)] = row.Value;
        }

        return result;
    }

    private static bool IsFederationEnabled(IReadOnlyDictionary<(ServiceId ServiceId, string StorageKey), string> values) =>
        values.TryGetValue((ServiceId.Federation, "Federation:Enabled"), out var value)
        && bool.TryParse(value, out var enabled)
        && enabled;

    private async Task<SettingRow?> GetLockedRowAsync(SettingsScope scope, string key, CancellationToken cancellationToken)
    {
        var settings = _context.Settings(scope);
        if (!_context.Database.IsRelational())
            return await settings.SingleOrDefaultAsync(row => row.Key == key, cancellationToken);

#pragma warning disable EF1002
        return await settings
            .FromSqlRaw($"SELECT * FROM \"{scope.TableName}\" WHERE \"Key\" = {{0}} FOR UPDATE", key)
            .SingleOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
    }

    private Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        _context.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(CancellationToken cancellationToken) =>
        await _context.Database.BeginTransactionAsync(cancellationToken);

    private Task<int> AcquireDatabaseLockAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(
                _context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            return Task.FromResult(0);

        return _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({SetupAdvisoryLockKey})",
            cancellationToken);
    }

    private static string NormalizeActor(string? actor) => string.IsNullOrWhiteSpace(actor) ? "setup" : actor.Trim();
}
