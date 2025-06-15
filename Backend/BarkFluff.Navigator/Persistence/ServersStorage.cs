namespace BarkFluff.Navigator.Persistence;

using Domain;
using Microsoft.EntityFrameworkCore;

public class ServersStorage
{
    private readonly NavigatorContext _context;

    public ServersStorage(NavigatorContext context)
    {
        _context = context;
    }

    public async Task<List<ServerInfo>> GetServers()
    {
        return await _context.Servers.ToListAsync();
    }
}