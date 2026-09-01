extern alias GrpcServer;

using BarkFluff.Settings.Catalog;
using BarkFluff.Settings.Domain;
using BarkFluff.Settings.Persistence.Contexts;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

using DependencyCheck = GrpcServer::BarkFluff.GrpcServer.DependencyCheck;
using IBarkFluffReadinessContributor = GrpcServer::BarkFluff.GrpcServer.IBarkFluffReadinessContributor;
using MetricsCollector = GrpcServer::BarkFluff.GrpcServer.Metrics.MetricsCollector;

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
        var values = new Dictionary<(ServiceId ServiceId, string StorageKey), string>();
        foreach (var group in SettingsCatalog.All.Where(entry => entry.Setup is not null).GroupBy(entry => entry.ServiceId))
        {
            var scope = SettingsScopes.Get(group.Key);
            var expected = group.ToDictionary(entry => entry.StorageKey, entry => entry);
            var rows = await _context.Settings(scope)
                .Where(row => expected.Keys.Contains(row.Key))
                .ToDictionaryAsync(row => row.Key, row => row.Value, cancellationToken);
            foreach (var (key, value) in rows)
                values[(group.Key, key)] = value;
        }

        var federationEnabled = values.TryGetValue((ServiceId.Federation, "Federation:Enabled"), out var enabledValue)
            && bool.TryParse(enabledValue, out var enabled)
            && enabled;

        foreach (var entry in SettingsCatalog.All.Where(entry => entry.RequiresManualValue))
        {
            if (entry.Setup is null || !SettingsSetupMetadata.IsApplicable(entry.Setup.Requirement, federationEnabled))
                continue;

            var value = values.GetValueOrDefault((entry.ServiceId, entry.StorageKey), string.Empty);
            var valid = !string.IsNullOrWhiteSpace(value)
                && SettingsSetupValidation.Validate(entry, value, value).IsValid;
            if (!valid)
                missing.Add($"{SettingsScopes.Get(entry.ServiceId).TableName}.{entry.StorageKey}");
        }

        missing.Sort(StringComparer.Ordinal);
        _metrics?.Set("settings_missing_manual_total", missing.Count);
        return missing.Count == 0
            ? new DependencyCheck("SettingsManualValues", "healthy", null, null)
            : new DependencyCheck("SettingsManualValues", "degraded", null, $"manual settings are empty: {string.Join(", ", missing)}");
    }
}
