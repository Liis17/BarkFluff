using BarkFluff.Bots.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Bots.Persistence.Services;

public class BotsStorage
{
    private readonly BotsContext _context;

    public BotsStorage(BotsContext context)
    {
        _context = context;
    }

    public Task<List<Bot>> GetAll()
        => _context.Bots.AsNoTracking().OrderBy(b => b.CreatedAt).ToListAsync();

    public Task<Bot?> GetById(long botId)
        => _context.Bots.FirstOrDefaultAsync(b => b.Id == botId);

    public Task<Bot?> GetByUsername(string username)
        => _context.Bots.FirstOrDefaultAsync(b => b.Username.ToLower() == username.ToLower());

    public Task<List<Bot>> GetByOwner(long ownerUserId)
        => _context.Bots.AsNoTracking().Where(b => b.OwnerUserId == ownerUserId).OrderBy(b => b.CreatedAt).ToListAsync();

    public async Task<Bot> Add(Bot bot)
    {
        await _context.Bots.AddAsync(bot);
        await _context.SaveChangesAsync();
        return bot;
    }

    public Task Update(Bot bot)
    {
        _context.Bots.Update(bot);
        return _context.SaveChangesAsync();
    }

    /// <summary>Удаляет бота; BotUpdates удаляются каскадом (FK CASCADE).</summary>
    public async Task Delete(long botId)
    {
        await _context.Bots.Where(b => b.Id == botId).ExecuteDeleteAsync();
    }
}
