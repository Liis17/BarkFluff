using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BarkFluff.Messages.Persistence.Services;

public class MessagesStorage
{
    private readonly MessagesContext _context;
    private readonly ChatsStorage _chatsStorage;

    public MessagesStorage(MessagesContext context, ChatsStorage chatsStorage)
    {
        _context = context;
        _chatsStorage = chatsStorage;
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

    /// <summary>
    /// Gets messages with bi-directional offset from a reference message.
    /// Returns messages before, the reference message itself, and messages after.
    /// </summary>
    /// <param name="chatId">Chat identifier</param>
    /// <param name="fromMessageId">Reference message ID (if null, returns latest messages)</param>
    /// <param name="offsetBefore">Number of messages before the reference message</param>
    /// <param name="offsetAfter">Number of messages after the reference message</param>
    /// <returns>List of messages ordered by sent time</returns>
    public async Task<List<Message>> GetChatMessagesWithOffset(Guid chatId, long? fromMessageId, int offsetBefore, int offsetAfter)
    {
        const int MaxOffset = 50;
        
        if (fromMessageId == null)
        {
            // If no reference message, return the latest messages (use offsetBefore as count, capped at MaxOffset)
            var count = offsetBefore > 0 ? Math.Min(offsetBefore, MaxOffset) : MaxOffset;
            var latestMessages = await _context
                .Messages
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(m => m.SentAt)
                .Take(count)
                .ToListAsync();

            return latestMessages.OrderBy(m => m.SentAt).ToList();
        }

        var referenceMessage = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == fromMessageId && m.ChatId == chatId);

        if (referenceMessage == null)
        {
            throw new FromMessageNotFoundException();
        }

        var result = new List<Message>();

        // Get messages before the reference message (older)
        if (offsetBefore > 0)
        {
            var messagesBefore = await _context
                .Messages
                .Where(x => x.ChatId == chatId && x.SentAt < referenceMessage.SentAt)
                .OrderByDescending(m => m.SentAt)
                .Take(offsetBefore)
                .ToListAsync();

            result.AddRange(messagesBefore);
        }

        // Add the reference message itself
        result.Add(referenceMessage);

        // Get messages after the reference message (newer)
        if (offsetAfter > 0)
        {
            var messagesAfter = await _context
                .Messages
                .Where(x => x.ChatId == chatId && x.SentAt > referenceMessage.SentAt)
                .OrderBy(m => m.SentAt)
                .Take(offsetAfter)
                .ToListAsync();

            result.AddRange(messagesAfter);
        }

        // Return messages sorted by sent time
        return result.OrderBy(m => m.SentAt).ToList();
    }

    public async Task<Message> AddMessage(Message message)
    {
        var result = await _context.Messages.AddAsync(message);
        await _context.SaveChangesAsync();

        return result.Entity;
    }

    public async Task<List<Message>> GetMessagesByIds(List<long> messageIds)
    {
        return await _context.Messages
            .Where(m => messageIds.Contains(m.Id))
            .ToListAsync();
    }

    public async Task MarkMessagesAsRead(List<long> messageIds, long userId)
    {
        // Use PostgreSQL-specific array operations for optimal performance
        await _context.Database.ExecuteSqlRawAsync(@"
            UPDATE ""Messages""
            SET ""ReadBy"" = CASE
                WHEN ""ReadBy"" @> ARRAY[@userId]::bigint[] THEN ""ReadBy""
                ELSE array_append(""ReadBy"", @userId)
            END
            WHERE ""Id"" = ANY(@messageIds)",
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@messageIds", messageIds.ToArray()));
    }
}