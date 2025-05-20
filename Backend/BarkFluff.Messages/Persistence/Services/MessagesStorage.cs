using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence.Services;

public class MessagesStorage
{
    private readonly MessagesContext _context;

    public MessagesStorage(MessagesContext context)
    {
        _context = context;
    }

    public async Task<List<Message>> GetChatMessages(Guid chatId, long? fromMessageId, int count)
    {
        
        var startDate = DateTime.MaxValue;

        if (fromMessageId != null)
        {
            var startMessage = _context.Messages.FirstOrDefault(m => m.Id == fromMessageId && m.ChatId == chatId);

            if (startMessage is null)
            {
                throw new FromMessageNotFoundException();
            }

            startDate = startMessage.SentAt;
        }


        var messages = await _context
            .Messages
            .OrderByDescending(m => m.SentAt)
            .Where(x => x.ChatId == chatId && x.SentAt <= startDate)
            .Take(count)
            .ToListAsync();

        return messages;
    }

    public async Task<Message> AddMessage(Message message)
    {
        var result = await _context.Messages.AddAsync(message);
        await _context.SaveChangesAsync();

        return result.Entity;
    }
}