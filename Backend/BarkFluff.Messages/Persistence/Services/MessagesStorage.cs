using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Exceptions;
using BarkFluff.Messages.Persistence.Services.Dtos;

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
            var startMessage = await _context.Messages
                .FirstOrDefaultAsync(m => m.Id == fromMessageId && m.ChatId == chatId && !m.IsDeleted);

            if (startMessage is null)
            {
                throw new FromMessageNotFoundException();
            }

            startDate = startMessage.SentAt;
        }


        var messages = await _context
            .Messages
            .OrderByDescending(m => m.SentAt)
            .Where(x => x.ChatId == chatId && !x.IsDeleted && x.SentAt <= startDate)
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
                .Where(x => x.ChatId == chatId && !x.IsDeleted)
                .OrderByDescending(m => m.SentAt)
                .Take(count)
                .ToListAsync();

            return latestMessages.OrderBy(m => m.SentAt).ToList();
        }

        var referenceMessage = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == fromMessageId && m.ChatId == chatId && !m.IsDeleted);

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
                .Where(x => x.ChatId == chatId && !x.IsDeleted && x.SentAt < referenceMessage.SentAt)
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
                .Where(x => x.ChatId == chatId && !x.IsDeleted && x.SentAt > referenceMessage.SentAt)
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
            .Where(m => messageIds.Contains(m.Id) && !m.IsDeleted)
            .ToListAsync();
    }

    public async Task<Message?> GetMessageById(long id)
    {
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
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
            WHERE ""Id"" = ANY(@messageIds) AND ""IsDeleted"" = false",
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@messageIds", messageIds.ToArray()));
    }

    /// <summary>
    /// Gets attachments from a chat with pagination, filtering, and sorting.
    /// </summary>
    public async Task<(List<ChatAttachmentDto> Attachments, int TotalCount)> GetChatAttachmentsAsync(
        Guid chatId,
        MessageAttachmentType? attachmentType,
        int skip,
        int take,
        bool sortDescending)
    {
        // Build filter and sort strings
        string attachmentTypeFilter;
        if (attachmentType.HasValue && attachmentType.Value != Domain.MessageAttachmentType.Unknown)
        {
            var typeValue = (int)attachmentType.Value;
            attachmentTypeFilter = "AND a.\"Type\" = " + typeValue;
        }
        else
        {
            attachmentTypeFilter = "AND a.\"Type\" != 8";
        }

        var sortOrder = sortDescending ? "DESC" : "ASC";

        // Get total count with filter (database-side)
        var countSql = $@"
            SELECT COUNT(*)
            FROM ""Messages"" m
            INNER JOIN ""MessageAttachments"" a ON m.""Id"" = a.""MessageId""
            WHERE m.""ChatId"" = @chatId
            AND m.""IsDeleted"" = false
            {attachmentTypeFilter}";

        var totalCount = (await _context.Database.SqlQueryRaw<int>(
            countSql,
            new NpgsqlParameter("@chatId", chatId))
            .ToListAsync()).FirstOrDefault();

        // Get attachments with pagination (database-side)
        var sql = $@"
            SELECT
                m.""Id"" AS ""MessageId"",
                m.""SenderId"" AS ""SenderId"",
                m.""SentAt"" AS ""SentAt"",
                a.""Id"" AS ""AttachmentId"",
                a.""Type"" AS ""AttachmentType"",
                a.""FileId"" AS ""FileId"",
                a.""PreviewUrl"" AS ""PreviewUrl"",
                a.""FileSize"" AS ""FileSize""
            FROM ""Messages"" m
            INNER JOIN ""MessageAttachments"" a ON m.""Id"" = a.""MessageId""
            WHERE m.""ChatId"" = @chatId
            AND m.""IsDeleted"" = false
            {attachmentTypeFilter}
            ORDER BY m.""SentAt"" {sortOrder}, a.""Id"" ASC
            LIMIT @take OFFSET @skip";

        var attachments = await _context.Database
            .SqlQueryRaw<ChatAttachmentDto>(sql,
                new NpgsqlParameter("@chatId", chatId),
                new NpgsqlParameter("@take", take),
                new NpgsqlParameter("@skip", skip))
            .ToListAsync();

        return (attachments, totalCount);
    }
}