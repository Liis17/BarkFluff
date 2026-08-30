using BarkFluff.Configuration.Domain;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Configuration.Persistence;

public class ConfigurationStorage
{
    private static readonly HashSet<string> SectionOnlyConfigurationSections =
        ["DevelopersDb", "Redis", "NavigatorUrl"];

    private readonly ConfigurationContext _context;
    private readonly MetricsCollector _metrics;

    public ConfigurationStorage(ConfigurationContext context, MetricsCollector metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<List<ConfigurationItem>> GetConfiguration(ServiceId serviceId)
    {
        var configurations = await _context.Configurations
            .AsNoTracking()
            .Where(x => x.ServiceId == serviceId || x.ServiceId == ServiceId.Unknown)
            .ToListAsync();

        return NormalizeSectionOnlyKeys(configurations);
    }

    public async Task<List<ConfigurationItem>> GetAllConfigurationsAsync()
    {
        var configurations = await _context.Configurations
            .AsNoTracking()
            .ToListAsync();

        return NormalizeSectionOnlyKeys(configurations);
    }

    public async Task UpdateConfigurationAsync(string section, string key, string value, ServiceId serviceId, string editedBy, string editedFrom)
    {
        var normalizedKey = NormalizeConfigurationKey(section, key);
        var existing = await _context.Configurations
            .FirstOrDefaultAsync(x => x.Section == section && x.Key == normalizedKey && x.ServiceId == serviceId);

        if (existing is null && IsSectionOnlyConfigurationSection(section) && normalizedKey.Length == 0)
        {
            existing = await _context.Configurations
                .FirstOrDefaultAsync(x => x.Section == section &&
                    x.ServiceId == serviceId &&
                    x.Key != null &&
                    x.Key.Trim() == string.Empty);

            if (existing is not null)
                existing.Key = normalizedKey;
        }

        var changedAt = DateTime.UtcNow;
        var previousValue = existing?.Value ?? string.Empty;

        if (existing != null)
        {
            existing.Value = value;
            existing.EditedAt = changedAt;
            existing.EditedBy = editedBy;
            existing.EditedFrom = editedFrom;
        }
        else
        {
            var newItem = new ConfigurationItem
            {
                Section = section,
                Key = normalizedKey,
                Value = value,
                EditedAt = changedAt,
                EditedBy = editedBy,
                EditedFrom = editedFrom,
                ServiceId = serviceId
            };
            await _context.Configurations.AddAsync(newItem);
            existing = newItem;
        }

        await _context.ConfigurationRevisions.AddAsync(new ConfigurationRevision
        {
            ConfigurationItem = existing,
            Section = section,
            Key = normalizedKey,
            ServiceId = serviceId,
            PreviousValue = previousValue,
            NewValue = value,
            ChangedAt = changedAt,
            ChangedBy = editedBy,
            ChangedFrom = editedFrom,
            ChangeKind = "Update"
        });

        await _context.SaveChangesAsync();
        _metrics.Increment("configurations_db_writes");

        // Обновляем gauge общего числа записей. Делаем после каждой записи —
        // изменения редкие (UpdateConfiguration вызывается админом, не клиентами).
        var total = await _context.Configurations.CountAsync();
        _metrics.Set("configurations_total", total);
    }

    public async Task<List<ConfigurationRevision>> GetConfigurationHistoryAsync(
        string section,
        string key,
        ServiceId serviceId,
        int count)
    {
        var normalizedKey = NormalizeConfigurationKey(section, key);

        return await _context.ConfigurationRevisions
            .AsNoTracking()
            .Where(x => x.Section == section &&
                x.ServiceId == serviceId &&
                (x.Key == normalizedKey ||
                    (IsSectionOnlyConfigurationSection(section) &&
                     normalizedKey.Length == 0 &&
                     x.Key != null &&
                     x.Key.Trim() == string.Empty)))
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(count, 1, 100))
            .ToListAsync();
    }

    public async Task RollbackConfigurationAsync(long revisionId, string editedBy, string editedFrom)
    {
        var sourceRevision = await _context.ConfigurationRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == revisionId)
            ?? throw new InvalidOperationException($"Ревизия {revisionId} не найдена");

        var configuration = await _context.Configurations
            .FirstOrDefaultAsync(x => x.Id == sourceRevision.ConfigurationItemId)
            ?? throw new InvalidOperationException("Строка конфигурации для ревизии не найдена");

        var changedAt = DateTime.UtcNow;
        var currentValue = configuration.Value;

        configuration.Value = sourceRevision.PreviousValue;
        configuration.EditedAt = changedAt;
        configuration.EditedBy = editedBy;
        configuration.EditedFrom = editedFrom;

        await _context.ConfigurationRevisions.AddAsync(new ConfigurationRevision
        {
            ConfigurationItemId = configuration.Id,
            Section = configuration.Section,
            Key = configuration.Key,
            ServiceId = configuration.ServiceId,
            PreviousValue = currentValue,
            NewValue = sourceRevision.PreviousValue,
            ChangedAt = changedAt,
            ChangedBy = editedBy,
            ChangedFrom = editedFrom,
            ChangeKind = "Rollback",
            SourceRevisionId = sourceRevision.Id
        });

        await _context.SaveChangesAsync();
        _metrics.Increment("configurations_db_writes");
    }

    private static List<ConfigurationItem> NormalizeSectionOnlyKeys(List<ConfigurationItem> configurations)
    {
        foreach (var configuration in configurations)
            configuration.Key = NormalizeConfigurationKey(configuration.Section, configuration.Key);

        return configurations;
    }

    private static string NormalizeConfigurationKey(string section, string? key) =>
        key is null || (IsSectionOnlyConfigurationSection(section) && string.IsNullOrWhiteSpace(key))
            ? string.Empty
            : key;

    private static bool IsSectionOnlyConfigurationSection(string section) =>
        SectionOnlyConfigurationSections.Contains(section);

    // ─── Reserved Names ─────────────────────────────────────────────────────────
    // Хранится как одна строка: Section="ReservedNames", Key="Usernames", Value="admin,support,help,..."

    private const string ReservedNamesSection = "ReservedNames";
    private const string ReservedNamesKey = "Usernames";

    private async Task<ConfigurationItem?> GetReservedNamesRow()
    {
        return await _context.Configurations
            .FirstOrDefaultAsync(x => x.Section == ReservedNamesSection && x.Key == ReservedNamesKey);
    }

    private static List<string> ParseNames(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
    }

    public async Task<List<string>> GetReservedNamesAsync()
    {
        var row = await _context.Configurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Section == ReservedNamesSection && x.Key == ReservedNamesKey);

        var names = row == null ? new List<string>() : ParseNames(row.Value);
        _metrics.Set("reserved_names_count", names.Count);
        return names;
    }

    public async Task AddReservedNameAsync(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Имя не может быть пустым");

        var row = await GetReservedNamesRow();

        if (row == null)
        {
            await _context.Configurations.AddAsync(new ConfigurationItem
            {
                Section = ReservedNamesSection,
                Key = ReservedNamesKey,
                Value = normalized,
                EditedAt = DateTime.UtcNow,
                EditedBy = "AdminPanel",
                EditedFrom = "AdminPanel",
                ServiceId = ServiceId.Unknown
            });
        }
        else
        {
            var names = ParseNames(row.Value);
            if (names.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Имя '{normalized}' уже зарезервировано");

            names.Add(normalized);
            row.Value = string.Join(",", names);
            row.EditedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        await UpdateReservedNamesGaugeAsync();
    }

    public async Task UpdateReservedNameAsync(string oldName, string newName)
    {
        var normalizedNew = newName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedNew))
            throw new ArgumentException("Новое имя не может быть пустым");

        var row = await GetReservedNamesRow();
        if (row == null)
            throw new InvalidOperationException($"Имя '{oldName}' не найдено");

        var names = ParseNames(row.Value);
        var index = names.FindIndex(n => n.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException($"Имя '{oldName}' не найдено");

        if (names.Any(n => n.Equals(normalizedNew, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Имя '{normalizedNew}' уже зарезервировано");

        names[index] = normalizedNew;
        row.Value = string.Join(",", names);
        row.EditedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await UpdateReservedNamesGaugeAsync();
    }

    public async Task DeleteReservedNameAsync(string name)
    {
        var row = await GetReservedNamesRow();
        if (row == null)
            throw new InvalidOperationException($"Имя '{name}' не найдено");

        var names = ParseNames(row.Value);
        var removed = names.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException($"Имя '{name}' не найдено");

        row.Value = string.Join(",", names);
        row.EditedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await UpdateReservedNamesGaugeAsync();
    }

    private async Task UpdateReservedNamesGaugeAsync()
    {
        var row = await _context.Configurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Section == ReservedNamesSection && x.Key == ReservedNamesKey);

        var count = row == null ? 0 : ParseNames(row.Value).Count;
        _metrics.Set("reserved_names_count", count);
    }
}
