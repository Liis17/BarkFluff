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
}