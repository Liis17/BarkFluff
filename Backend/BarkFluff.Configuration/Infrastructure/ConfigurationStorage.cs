using BarkFluff.Configuration.Domain;
using BarkFluff.Shared.Identity;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Configuration.Infrastructure;

public class ConfigurationStorage
{
    private readonly ConfigurationContext _context;

    public ConfigurationStorage(ConfigurationContext context)
    {
        _context = context;
    }

    public async Task<List<ConfigurationItem>> GetConfiguration(ServiceId serviceId)
    {
        var configurations = await _context.Configurations
            .AsNoTracking()
            .Where(x => x.ServiceId == serviceId || x.ServiceId == ServiceId.Unknown)
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
    }

    // ─── Reserved Names ─────────────────────────────────────────────────────────

    private const string ReservedNamesSection = "ReservedNames";

    public async Task<List<string>> GetReservedNamesAsync()
    {
        return await _context.Configurations
            .AsNoTracking()
            .Where(x => x.Section == ReservedNamesSection && x.ServiceId == ServiceId.Unknown)
            .Select(x => x.Key)
            .ToListAsync();
    }

    public async Task AddReservedNameAsync(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Имя не может быть пустым");

        var exists = await _context.Configurations
            .AnyAsync(x => x.Section == ReservedNamesSection && x.Key == normalized && x.ServiceId == ServiceId.Unknown);

        if (exists)
            throw new InvalidOperationException($"Имя '{normalized}' уже зарезервировано");

        await _context.Configurations.AddAsync(new ConfigurationItem
        {
            Section = ReservedNamesSection,
            Key = normalized,
            Value = normalized,
            EditedAt = DateTime.UtcNow,
            EditedBy = "AdminPanel",
            EditedFrom = "AdminPanel",
            ServiceId = ServiceId.Unknown
        });

        await _context.SaveChangesAsync();
    }

    public async Task UpdateReservedNameAsync(string oldName, string newName)
    {
        var normalizedNew = newName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedNew))
            throw new ArgumentException("Новое имя не может быть пустым");

        var existing = await _context.Configurations
            .FirstOrDefaultAsync(x => x.Section == ReservedNamesSection && x.Key == oldName && x.ServiceId == ServiceId.Unknown);

        if (existing == null)
            throw new InvalidOperationException($"Имя '{oldName}' не найдено");

        var duplicate = await _context.Configurations
            .AnyAsync(x => x.Section == ReservedNamesSection && x.Key == normalizedNew && x.ServiceId == ServiceId.Unknown);

        if (duplicate)
            throw new InvalidOperationException($"Имя '{normalizedNew}' уже зарезервировано");

        existing.Key = normalizedNew;
        existing.Value = normalizedNew;
        existing.EditedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteReservedNameAsync(string name)
    {
        var existing = await _context.Configurations
            .FirstOrDefaultAsync(x => x.Section == ReservedNamesSection && x.Key == name && x.ServiceId == ServiceId.Unknown);

        if (existing == null)
            throw new InvalidOperationException($"Имя '{name}' не найдено");

        _context.Configurations.Remove(existing);
        await _context.SaveChangesAsync();
    }
}