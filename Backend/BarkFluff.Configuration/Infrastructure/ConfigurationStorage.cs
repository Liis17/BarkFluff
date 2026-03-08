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
}