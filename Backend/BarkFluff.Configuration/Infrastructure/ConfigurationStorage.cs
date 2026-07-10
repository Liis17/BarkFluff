using BarkFluff.Configuration.Domain;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Configuration.Infrastructure;

public class ConfigurationStorage
{
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

        return configurations;
    }

    public async Task<List<ConfigurationItem>> GetAllConfigurationsAsync()
    {
        var configurations = await _context.Configurations
            .AsNoTracking()
            .ToListAsync();

        return configurations;
    }

    public async Task UpdateConfigurationAsync(string section, string key, string value, ServiceId serviceId, string editedBy, string editedFrom)
    {
        var existing = await _context.Configurations
            .FirstOrDefaultAsync(x => x.Section == section && x.Key == key && x.ServiceId == serviceId);

        if (existing != null)
        {
            existing.Value = value;
            existing.EditedAt = DateTime.UtcNow;
            existing.EditedBy = editedBy;
            existing.EditedFrom = editedFrom;
        }
        else
        {
            var newItem = new ConfigurationItem
            {
                Section = section,
                Key = key,
                Value = value,
                EditedAt = DateTime.UtcNow,
                EditedBy = editedBy,
                EditedFrom = editedFrom,
                ServiceId = serviceId
            };
            await _context.Configurations.AddAsync(newItem);
        }

        await _context.SaveChangesAsync();
        _metrics.Increment("configurations_db_writes");

        // Обновляем gauge общего числа записей. Делаем после каждой записи —
        // изменения редкие (UpdateConfiguration вызывается админом, не клиентами).
        var total = await _context.Configurations.CountAsync();
        _metrics.Set("configurations_total", total);
    }

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