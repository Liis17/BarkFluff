using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence.Services;

public class PinnedMessagesStorage
{
    private readonly MessagesContext _context;

    public PinnedMessagesStorage(MessagesContext context)
    {
        _context = context;
    }

    public Task<PinnedMessage?> GetPinByMessageIdAsync(Guid chatId, long messageId)
    {
        return _context.PinnedMessages
            .FirstOrDefaultAsync(x => x.ChatId == chatId && x.MessageId == messageId);
    }

    public Task<List<PinnedMessage>> ListByChatAsync(Guid chatId, int skip, int count)
    {
        return _context.PinnedMessages
            .Where(x => x.ChatId == chatId)
            .OrderByDescending(x => x.PinnedAt)
            .Skip(skip)
            .Take(count)
            .ToListAsync();
    }

    public Task<int> CountByChatAsync(Guid chatId)
    {
        return _context.PinnedMessages.CountAsync(x => x.ChatId == chatId);
    }

    public async Task AddAsync(PinnedMessage pin)
    {
        await _context.PinnedMessages.AddAsync(pin);
    }

    public void Remove(PinnedMessage pin)
    {
        _context.PinnedMessages.Remove(pin);
    }

    public async Task<int> RemoveAllByChatAsync(Guid chatId)
    {
        var pins = await _context.PinnedMessages
            .Where(x => x.ChatId == chatId)
            .ToListAsync();

        _context.PinnedMessages.RemoveRange(pins);

        return pins.Count;
    }

    public async Task<PinnedMessage?> RemoveByMessageIdAsync(long messageId)
    {
        var pin = await _context.PinnedMessages.FirstOrDefaultAsync(x => x.MessageId == messageId);

        if (pin is null)
        {
            return null;
        }

        _context.PinnedMessages.Remove(pin);

        return pin;
    }

    public Task SaveChangesAsync()
    {
        return _context.SaveChangesAsync();
    }
}
