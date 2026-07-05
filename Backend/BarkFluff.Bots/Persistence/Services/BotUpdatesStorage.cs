using BarkFluff.Bots.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Bots.Persistence.Services;

public class BotUpdatesStorage
{
    private readonly BotsContext _context;

    public BotUpdatesStorage(BotsContext context)
    {
        _context = context;
    }

    /// <summary>Сохранить update, возвращает присвоенный update_id (IDENTITY).</summary>
    public async Task<long> Add(long botId, string payloadJson)
    {
        var update = new BotUpdate
        {
            BotId = botId,
            Payload = payloadJson,
            CreatedAt = DateTime.UtcNow,
        };

        await _context.BotUpdates.AddAsync(update);
        await _context.SaveChangesAsync();

        return update.Id;
    }

    /// <summary>Backlog update'ов бота с id >= fromUpdateId (0 = все неудалённые).</summary>
    public Task<List<BotUpdate>> GetBacklog(long botId, long fromUpdateId, int limit)
        => _context.BotUpdates.AsNoTracking()
            .Where(u => u.BotId == botId && u.Id >= fromUpdateId)
            .OrderBy(u => u.Id)
            .Take(limit)
            .ToListAsync();

    /// <summary>
    /// Подтвердить update'ы с id &lt; offset: удалить их и обновить LastConfirmedUpdateId.
    /// </summary>
    public async Task Confirm(long botId, long offset)
    {
        if (offset <= 0)
            return;

        await _context.BotUpdates
            .Where(u => u.BotId == botId && u.Id < offset)
            .ExecuteDeleteAsync();

        await _context.Bots
            .Where(b => b.Id == botId && b.LastConfirmedUpdateId < offset - 1)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.LastConfirmedUpdateId, offset - 1));
    }

    /// <summary>Ретеншн: удалить update'ы старше maxAge.</summary>
    public Task<int> DeleteOlderThan(DateTime cutoff)
        => _context.BotUpdates.Where(u => u.CreatedAt < cutoff).ExecuteDeleteAsync();
}
