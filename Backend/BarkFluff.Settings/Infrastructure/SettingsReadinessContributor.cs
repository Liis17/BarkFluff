using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Settings.Infrastructure;

public sealed class SettingsReadinessContributor : IBarkFluffReadinessContributor
{
    private readonly SettingsContext _context;
    private readonly MetricsCollector? _metrics;

    public SettingsReadinessContributor(SettingsContext context, MetricsCollector? metrics = null)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<DependencyCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        foreach (var group in SettingsCatalog.All.Where(entry => entry.RequiresManualValue).GroupBy(entry => entry.ServiceId))
        {
            var scope = SettingsScopes.Get(group.Key);
            var expected = group.ToDictionary(entry => entry.StorageKey, entry => entry);
            var values = await _context.Settings(scope)
                .Where(row => expected.Keys.Contains(row.Key))
                .ToDictionaryAsync(row => row.Key, row => row.Value, cancellationToken);
            missing.AddRange(expected.Values
                .Where(entry => !values.TryGetValue(entry.StorageKey, out var value) || string.IsNullOrWhiteSpace(value))
                .Select(entry => $"{scope.TableName}.{entry.StorageKey}"));
        }

        missing.Sort(StringComparer.Ordinal);
        _metrics?.Set("settings_missing_manual_total", missing.Count);
        return missing.Count == 0
            ? new DependencyCheck("SettingsManualValues", "healthy", null, null)
            : new DependencyCheck("SettingsManualValues", "degraded", null, $"manual settings are empty: {string.Join(", ", missing)}");
    }
}
