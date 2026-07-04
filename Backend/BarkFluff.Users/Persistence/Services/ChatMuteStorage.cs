using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class ChatMuteStorage(UsersContext context)
{
    // Замьютить чат (upsert). mutedUntil == null => навсегда.
    public async Task SetMuted(long userId, Guid chatId, DateTime? mutedUntil)
    {
        var existing = await context.ChatMutes
            .FirstOrDefaultAsync(m => m.UserId == userId && m.ChatId == chatId);

        if (existing is null)
        {
            context.ChatMutes.Add(new ChatMute
            {
                UserId = userId,
                ChatId = chatId,
                MutedUntil = mutedUntil,
            });
        }
        else
        {
            existing.MutedUntil = mutedUntil;
        }

        await context.SaveChangesAsync();
    }

    // Снять mute (удалить запись, если есть).
    public async Task Unmute(long userId, Guid chatId)
    {
        var existing = await context.ChatMutes
            .FirstOrDefaultAsync(m => m.UserId == userId && m.ChatId == chatId);

        if (existing is null)
        {
            return;
        }

        context.ChatMutes.Remove(existing);
        await context.SaveChangesAsync();
    }

    // Активные муьюты пользователя (для GetMutedChats на клиенте).
    public async Task<List<ChatMute>> GetActiveMutes(long userId)
    {
        var now = DateTime.UtcNow;
        return await context.ChatMutes
            .Where(m => m.UserId == userId && (m.MutedUntil == null || m.MutedUntil > now))
            .ToListAsync();
    }

    // Подмножество chatIds с активным mute (для флага muted в Messages).
    public async Task<List<Guid>> GetMutedChatIds(long userId, IReadOnlyCollection<Guid> chatIds)
    {
        if (chatIds.Count == 0)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        return await context.ChatMutes
            .Where(m => m.UserId == userId
                        && chatIds.Contains(m.ChatId)
                        && (m.MutedUntil == null || m.MutedUntil > now))
            .Select(m => m.ChatId)
            .ToListAsync();
    }
}
