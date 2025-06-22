namespace BarkFluff.Navigator.Persistence;

using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

public class ServersStorage
{
    private readonly IMemoryCache memoryCache;

    public ServersStorage(IMemoryCache memoryCache)
    {
        this.memoryCache = memoryCache;
    }

    public async Task<List<ServerInfo>> GetServers()
    {
        return null;
    }
}