using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Exceptions;
using BarkFluff.Messages.Persistence.Services.Dtos;
using BarkFluff.Shared.Queue.Messages;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using System.Text.Json;

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
            .AsNoTracking()
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
                .AsNoTracking()
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
                .AsNoTracking()
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
                .AsNoTracking()
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
        message.LastChangeAt = message.SentAt;

        var result = await _context.Messages.AddAsync(message);
        await _context.SaveChangesAsync();

        return result.Entity;
    }

    public Task<Message?> GetByClientOperationIdAsync(
        long senderId,
        Guid clientOperationId,
        CancellationToken cancellationToken = default)
    {
        return _context.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                message => message.SenderId == senderId && message.ClientOperationId == clientOperationId,
                cancellationToken);
    }

    public async Task<(Message Message, bool Created)> AddMessageWithOutboxAsync(
        Message message,
        Func<Message, NewMessageEvent> eventFactory,
        CancellationToken cancellationToken)
    {
        if (message.ClientOperationId is { } operationId && message.SenderId is { } senderId)
        {
            var existing = await GetByClientOperationIdAsync(senderId, operationId, cancellationToken);
            if (existing is not null)
                return (existing, false);
        }

        try
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction is not null)
                return await AddMessageAndOutboxRowsAsync(message, eventFactory, cancellationToken);

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                var result = await AddMessageAndOutboxRowsAsync(message, eventFactory, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (DbUpdateException) when (message.ClientOperationId.HasValue && message.SenderId.HasValue)
        {
            _context.ChangeTracker.Clear();
            var winner = await GetByClientOperationIdAsync(
                message.SenderId.Value,
                message.ClientOperationId.Value,
                cancellationToken);
            if (winner is not null)
                return (winner, false);
            throw;
        }
    }

    private async Task<(Message Message, bool Created)> AddMessageAndOutboxRowsAsync(
        Message message,
        Func<Message, NewMessageEvent> eventFactory,
        CancellationToken cancellationToken)
    {
        message.LastChangeAt = message.SentAt;
        await _context.Messages.AddAsync(message, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var queueEvent = eventFactory(message);
        if (queueEvent.EventId == Guid.Empty)
            queueEvent.EventId = Guid.NewGuid();

        var now = DateTime.UtcNow;
        _context.MessageOutbox.Add(new MessageOutboxEntry
        {
            EventId = queueEvent.EventId,
            MessageId = message.Id,
            Payload = JsonSerializer.SerializeToUtf8Bytes(queueEvent),
            CreatedAt = now,
            NextAttemptAt = now,
            Status = MessageOutboxStatus.Pending,
        });
        await _context.SaveChangesAsync(cancellationToken);
        return (message, true);
    }

    public async Task<List<Message>> GetMessagesByIds(
        List<long> messageIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Messages
            .Where(m => messageIds.Contains(m.Id) && !m.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Message>> GetMessagesByIdsInChatAsync(Guid chatId, List<long> messageIds)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        return await _context.Messages
            .Where(m => m.ChatId == chatId && messageIds.Contains(m.Id) && !m.IsDeleted)
            .ToListAsync();
    }

    // Отличается от GetMessagesByIds тем, что НЕ отбрасывает удалённые: цитата на удалённое
    // сообщение должна отрисоваться как «сообщение удалено», а не исчезнуть без следа.
    public async Task<List<Message>> GetMessagesByIdsIncludingDeletedAsync(
        List<long> messageIds,
        CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        return await _context.Messages
            .Where(m => messageIds.Contains(m.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<Message?> GetMessageById(long id, CancellationToken cancellationToken = default)
    {
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    // Поиск fed-сообщения по стабильному межнодовому id (docs/rearch/05, ApplyFederatedEdit/Delete,
    // этап 2.4). Не фильтрует IsDeleted — удаление терминально, и LWW должен видеть текущее состояние.
    public async Task<Message?> GetByFederatedIdAsync(Guid chatId, Guid federatedId)
    {
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.ChatId == chatId && m.FederatedId == federatedId);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    public async Task MarkMessagesAsRead(List<long> messageIds, long userId)
    {
        if (_context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            var messages = await _context.Messages
                .Where(message => messageIds.Contains(message.Id) && !message.IsDeleted)
                .ToListAsync();

            foreach (var message in messages.Where(message => !message.ReadBy.Contains(userId)))
            {
                message.ReadBy.Add(userId);
            }

            await _context.SaveChangesAsync();
            return;
        }

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
        bool sortDescending,
        string? fileNameQuery = null)
    {
        var normalizedFileNameQuery = fileNameQuery?.Trim();
        var query = _context.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted && m.Content != null)
            .SelectMany(m => m.Content!.Attachments!, (message, attachment) => new { message, attachment });

        if (attachmentType.HasValue && attachmentType.Value != MessageAttachmentType.Unknown)
            query = query.Where(x => x.attachment.Type == attachmentType.Value);
        else
            query = query.Where(x => x.attachment.Type != MessageAttachmentType.ForwardedMessage);

        if (!string.IsNullOrEmpty(normalizedFileNameQuery))
        {
            var normalizedForComparison = normalizedFileNameQuery.ToUpper();
            query = query.Where(x => x.attachment.FileName != null
                && x.attachment.FileName.ToUpper().Contains(normalizedForComparison));
        }

        var totalCount = await query.CountAsync();
        var ordered = sortDescending
            ? query.OrderByDescending(x => x.message.SentAt).ThenBy(x => x.attachment.Id)
            : query.OrderBy(x => x.message.SentAt).ThenBy(x => x.attachment.Id);
        var attachments = await ordered
            .Skip(skip)
            .Take(take)
            .Select(x => new ChatAttachmentDto
            {
                MessageId = x.message.Id,
                SenderId = x.message.SenderId ?? 0,
                SentAt = x.message.SentAt,
                AttachmentId = x.attachment.Id,
                AttachmentType = x.attachment.Type,
                FileId = x.attachment.FileId ?? string.Empty,
                PreviewUrl = x.attachment.PreviewUrl,
                FileSize = x.attachment.FileSize,
                OriginServer = x.attachment.OriginServer,
                FileName = x.attachment.FileName,
                PreviewFileId = x.attachment.PreviewFileId,
                ImageWidth = x.attachment.ImageWidth,
                ImageHeight = x.attachment.ImageHeight
            })
            .ToListAsync();

        return (attachments, totalCount);
    }

    /// <summary>
    /// Возвращает локальные документы без сохранённого имени. Имена старых вложений
    /// лениво заполняются перед серверным поиском через Files.
    /// </summary>
    public async Task<List<string>> GetDocumentFileIdsMissingNamesAsync(Guid chatId)
    {
        return await _context.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted && m.Content != null)
            .SelectMany(m => m.Content!.Attachments!)
            .Where(a => a.Type == MessageAttachmentType.Document
                && a.OriginServer == null
                && a.FileName == null
                && a.FileId != null)
            .Select(a => a.FileId!)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>Сохраняет имена локальных документов, полученные из Files для legacy-вложений.</summary>
    public async Task SetDocumentFileNamesAsync(IReadOnlyDictionary<string, string> fileNames)
    {
        if (fileNames.Count == 0)
            return;

        var fileIds = fileNames.Keys.ToList();
        var messages = await _context.Messages
            .Include(m => m.Content!.Attachments)
            .Where(m => !m.IsDeleted && m.Content != null
                && m.Content.Attachments!.Any(a => a.Type == MessageAttachmentType.Document
                    && a.OriginServer == null
                    && a.FileName == null
                    && a.FileId != null
                    && fileIds.Contains(a.FileId)))
            .ToListAsync();

        foreach (var attachment in messages.SelectMany(m => m.Content!.Attachments!))
        {
            if (attachment.Type == MessageAttachmentType.Document
                && attachment.OriginServer == null
                && attachment.FileName == null
                && attachment.FileId != null
                && fileNames.TryGetValue(attachment.FileId, out var fileName))
            {
                attachment.FileName = fileName;
            }
        }

        await _context.SaveChangesAsync();
    }
}
