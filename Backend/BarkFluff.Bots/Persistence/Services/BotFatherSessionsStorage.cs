using BarkFluff.Bots.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Bots.Persistence.Services;

public class BotFatherSessionsStorage
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    private readonly BotsContext _context;

    public BotFatherSessionsStorage(BotsContext context)
    {
        _context = context;
    }

    /// <summary>Сессия пользователя; протухшая (>30 мин) сбрасывается в Idle.</summary>
    public async Task<BotFatherSession> GetOrCreate(long userId)
    {
        var session = await _context.BotFatherSessions.FirstOrDefaultAsync(s => s.UserId == userId);

        if (session is null)
        {
            session = new BotFatherSession { UserId = userId, UpdatedAt = DateTime.UtcNow };
            await _context.BotFatherSessions.AddAsync(session);
            await _context.SaveChangesAsync();
            return session;
        }

        if (session.UpdatedAt < DateTime.UtcNow - SessionTtl)
        {
            session.State = 0;
            session.ContextBotId = null;
            session.PendingName = null;
        }

        return session;
    }

    public Task Save(BotFatherSession session)
    {
        session.UpdatedAt = DateTime.UtcNow;
        return _context.SaveChangesAsync();
    }
}
