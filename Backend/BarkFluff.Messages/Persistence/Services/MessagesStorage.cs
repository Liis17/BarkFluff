using BarkFluff.Messages.Domain;
using BarkFluff.GrpcServer.Tracing;
using BarkFluff.Messages.Persistence.Exceptions;
using BarkFluff.Messages.Persistence.Services.Dtos;
using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;
using NpgsqlTypes;

using System.Text.Json;
using System.Diagnostics;

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

    public async Task<List<Message>> SearchMessages(
        long userId,
        MessageSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var visibleChatIds = _context.ChatMembers
            .Where(member => member.UserId == userId && member.Chat.Type == ChatType.Regular)
            .Where(member => member.HiddenAt == null || _context.Messages.Any(message =>
                message.ChatId == member.ChatId && !message.IsDeleted && message.SentAt > member.HiddenAt))
            .Select(member => member.ChatId);
        var query = _context.Messages.AsNoTracking()
            .Where(message => !message.IsDeleted && message.Type != MessageContentType.System
                && message.Content != null && visibleChatIds.Contains(message.ChatId));
        if (!string.IsNullOrEmpty(filter.Text))
        {
            var escaped = filter.Text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(message => EF.Functions.ILike(message.Content!.Text!, pattern, "\\"));
        }
        if (filter.AuthorUserId is { } authorId)
            query = query.Where(message => message.SenderId == authorId);
        if (filter.AuthorUserUuid is { } authorUuid)
            query = query.Where(message => message.SenderUuid == authorUuid);
        if (filter.SentFrom is { } sentFrom)
            query = query.Where(message => message.SentAt >= sentFrom);
        if (filter.SentBefore is { } sentBefore)
            query = query.Where(message => message.SentAt < sentBefore);
        if (filter.HasAttachments is { } hasAttachments)
            query = query.Where(message => (message.Content!.Attachments != null
                && message.Content.Attachments.Any(attachment => attachment.Type != MessageAttachmentType.Unknown
                    && attachment.Type != MessageAttachmentType.ForwardedMessage)) == hasAttachments);
        if (filter.AttachmentTypes.Count > 0)
        {
            var types = filter.AttachmentTypes.ToArray();
            query = query.Where(message => message.Content!.Attachments != null
                && message.Content.Attachments.Any(attachment => types.Contains(attachment.Type)));
        }
        if (filter.CursorSentAt is { } cursorSentAt)
            query = query.Where(message => message.SentAt < cursorSentAt
                || (message.SentAt == cursorSentAt && message.Id < filter.CursorMessageId));
        return await query.OrderByDescending(message => message.SentAt).ThenByDescending(message => message.Id)
            .Take(Math.Clamp(filter.PageSize, 1, 50) + 1)
            .ToListAsync(cancellationToken);
    }

    public Task<List<MessageSearchChat>> GetSearchChats(
        long userId,
        IReadOnlyList<Guid> chatIds,
        CancellationToken cancellationToken = default)
    {
        return _context.Chats.AsNoTracking().Where(chat => chatIds.Contains(chat.Id))
            .Select(chat => new MessageSearchChat(
                chat.Id, chat.Title, chat.Picture, chat.IsGroupChat,
                chat.IsGroupChat ? null : chat.Members!.Where(member => member.UserId != userId)
                    .Select(member => member.UserId).FirstOrDefault(),
                chat.IsGroupChat ? null : chat.Members!.Where(member => member.UserId != userId)
                    .Select(member => member.UserUuid).FirstOrDefault()))
            .ToListAsync(cancellationToken);
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

        var messages = await _context.Messages.AsNoTracking()
            .Where(m => m.ChatId == chatId && m.SentAt <= startDate && !m.IsDeleted)
            .OrderByDescending(message => message.SentAt)
            .ThenByDescending(message => message.Id)
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
                .ThenByDescending(m => m.Id)
                .Take(count)
                .ToListAsync();

            return latestMessages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();
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
                .Where(x => x.ChatId == chatId && !x.IsDeleted &&
                    (x.SentAt < referenceMessage.SentAt || (x.SentAt == referenceMessage.SentAt && x.Id < referenceMessage.Id)))
                .OrderByDescending(m => m.SentAt)
                .ThenByDescending(m => m.Id)
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
                .Where(x => x.ChatId == chatId && !x.IsDeleted &&
                    (x.SentAt > referenceMessage.SentAt || (x.SentAt == referenceMessage.SentAt && x.Id > referenceMessage.Id)))
                .OrderBy(m => m.SentAt)
                .ThenBy(m => m.Id)
                .Take(offsetAfter)
                .ToListAsync();

            result.AddRange(messagesAfter);
        }

        // Return messages sorted by sent time
        return result.OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();
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
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            CorrelationId = TraceContext.CorrelationId,
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

    /// <summary>
    /// DeleteChat(delete_for_everyone=true): та же очистка, что делает DeleteMessage
    /// для одного сообщения (IsDeleted + ClearContent), применённая ко всем ещё
    /// неудалённым сообщениям чата одним SaveChanges. Возвращает id очищенных сообщений
    /// (для рассылки MessageUnpinnedEvent по тем из них, что были закреплены).
    /// </summary>
    public async Task<List<long>> SoftDeleteAllInChatAsync(Guid chatId)
    {
        var messages = await _context.Messages
            .Where(m => m.ChatId == chatId && !m.IsDeleted)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var message in messages)
        {
            message.IsDeleted = true;
            message.ClearContent();
            message.LastChangeAt = now;
        }

        if (messages.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        return messages.Select(m => m.Id).ToList();
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

    public async Task<int> MarkMessagesAsReadWithOutboxAsync(
        IReadOnlyCollection<long> messageIds,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0) return 0;
        var ids = messageIds.Distinct().ToArray();
        return await ExecuteReadTransactionAsync(async transaction =>
        {
            var chatIds = await _context.Messages.AsNoTracking()
                .Where(message => ids.Contains(message.Id) && !message.IsDeleted)
                .Select(message => message.ChatId)
                .Distinct()
                .OrderBy(chatId => chatId)
                .ToListAsync(cancellationToken);
            if (_context.Database.IsNpgsql())
                await LockChatsAsync(chatIds, transaction!, cancellationToken);

            foreach (var chatId in chatIds)
            {
                if (!await _context.ChatMembers.AnyAsync(member => member.ChatId == chatId && member.UserId == userId,
                        cancellationToken))
                    throw new NoAccessToChatException();
            }

            List<long> candidates;
            if (_context.Database.IsNpgsql())
            {
                candidates = await SelectLockedMessageIdsAsync(
                    "\"Id\" = ANY(@ids) AND \"IsDeleted\" = FALSE AND @userId <> ALL(\"ReadBy\")",
                    "ORDER BY \"ChatId\", \"Id\" FOR UPDATE",
                    command =>
                    {
                        command.Parameters.Add(new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ids });
                        command.Parameters.AddWithValue("@userId", userId);
                    }, transaction!, cancellationToken);
            }
            else
            {
                var messages = await _context.Messages
                    .Where(message => ids.Contains(message.Id) && !message.IsDeleted)
                    .ToListAsync(cancellationToken);
                candidates = messages.Where(message => !message.ReadBy.Contains(userId))
                    .Select(message => message.Id).ToList();
            }

            var now = DateTime.UtcNow;
            var changed = _context.Database.IsNpgsql()
                ? await AppendReaderAsync(candidates, userId, transaction!, cancellationToken)
                : await MarkTrackedMessagesAsync(candidates, userId, cancellationToken);
            await WriteReadOutboxAsync(changed, userId, now, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return changed.Count;
        }, cancellationToken);
    }

    public async Task<(int ChangedCount, bool HasMore)> MarkChatMessagesAsReadUpToWithOutboxAsync(
        Guid chatId,
        long upToMessageId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteReadTransactionAsync(async transaction =>
        {
            if (_context.Database.IsNpgsql())
                await LockChatsAsync([chatId], transaction!, cancellationToken);
            if (!await _context.ChatMembers.AnyAsync(member => member.ChatId == chatId && member.UserId == userId,
                    cancellationToken))
                throw new NoAccessToChatException();

            List<long> candidates;
            if (_context.Database.IsNpgsql())
            {
                candidates = await SelectLockedMessageIdsAsync(
                    "\"ChatId\" = @chatId AND \"Id\" <= @upTo AND \"IsDeleted\" = FALSE AND @userId <> ALL(\"ReadBy\")",
                    "ORDER BY \"Id\" LIMIT 101 FOR UPDATE",
                    command =>
                    {
                        command.Parameters.AddWithValue("@chatId", NpgsqlDbType.Uuid, chatId);
                        command.Parameters.AddWithValue("@upTo", upToMessageId);
                        command.Parameters.AddWithValue("@userId", userId);
                    }, transaction!, cancellationToken);
            }
            else
            {
                candidates = await _context.Messages
                    .Where(message => message.ChatId == chatId && message.Id <= upToMessageId && !message.IsDeleted)
                    .OrderBy(message => message.Id)
                    .Select(message => new { message.Id, message.ReadBy })
                    .Where(message => !message.ReadBy.Contains(userId))
                    .Take(101)
                    .Select(message => message.Id)
                    .ToListAsync(cancellationToken);
            }

            var hasMore = candidates.Count > 100;
            var changedIds = candidates.Take(100).ToArray();
            var now = DateTime.UtcNow;
            var changed = _context.Database.IsNpgsql()
                ? await AppendReaderAsync(changedIds, userId, transaction!, cancellationToken)
                : await MarkTrackedMessagesAsync(changedIds, userId, cancellationToken);
            await WriteReadOutboxAsync(changed, userId, now, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return (changed.Count, hasMore);
        }, cancellationToken);
    }

    private async Task<T> ExecuteReadTransactionAsync<T>(
        Func<IDbContextTransaction?, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        async Task<T> RunOwnedAttemptAsync()
        {
            var checkpoint = CaptureReadTransactionCheckpoint();
            try
            {
                await using var transaction = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;
                var result = await operation(transaction);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                RestoreReadTransactionCheckpoint(checkpoint);
                throw;
            }
        }

        if (_context.Database.CurrentTransaction is { } existingTransaction)
        {
            var checkpoint = CaptureReadTransactionCheckpoint();
            try
            {
                return await operation(existingTransaction);
            }
            catch
            {
                RestoreReadTransactionCheckpoint(checkpoint);
                throw;
            }
        }

        if (!_context.Database.IsRelational())
            return await RunOwnedAttemptAsync();

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(RunOwnedAttemptAsync);
    }

    private ReadTransactionCheckpoint CaptureReadTransactionCheckpoint()
    {
        var entries = _context.ChangeTracker.Entries()
            .ToDictionary(entry => entry.Entity, entry => new TrackedEntryCheckpoint(
                entry.State,
                entry.CurrentValues.Clone(),
                entry.OriginalValues.Clone(),
                entry.Properties.ToDictionary(property => property.Metadata.Name, property => property.IsModified)),
                ReferenceEqualityComparer.Instance);
        return new ReadTransactionCheckpoint(entries);
    }

    private void RestoreReadTransactionCheckpoint(ReadTransactionCheckpoint checkpoint)
    {
        foreach (var entry in _context.ChangeTracker.Entries().ToArray())
        {
            if (!checkpoint.Entries.TryGetValue(entry.Entity, out var saved))
            {
                entry.State = EntityState.Detached;
                continue;
            }

            entry.State = saved.State;
            entry.CurrentValues.SetValues(saved.CurrentValues);
            entry.OriginalValues.SetValues(saved.OriginalValues);
            foreach (var property in entry.Properties)
                if (saved.ModifiedProperties.TryGetValue(property.Metadata.Name, out var wasModified))
                    property.IsModified = wasModified;
        }
    }

    private async Task<List<long>> SelectLockedMessageIdsAsync(
        string predicate,
        string orderBySuffix,
        Action<NpgsqlCommand> configure,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            $"SELECT \"Id\" FROM \"Messages\" WHERE {predicate} {orderBySuffix}",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        configure(command);
        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetInt64(0));
        return ids;
    }

    private async Task LockChatsAsync(
        IReadOnlyCollection<Guid> chatIds,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (chatIds.Count == 0) return;
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT \"Id\" FROM \"Chats\" WHERE \"Id\" = ANY(@chatIds) ORDER BY \"Id\" FOR UPDATE",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.Add(new NpgsqlParameter("@chatIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = chatIds.ToArray()
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) { }
    }

    private async Task<List<Message>> AppendReaderAsync(
        IReadOnlyCollection<long> messageIds,
        long userId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return [];
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand("""
            UPDATE "Messages"
            SET "ReadBy" = array_append("ReadBy", @userId)
            WHERE "Id" = ANY(@ids) AND "IsDeleted" = FALSE AND @userId <> ALL("ReadBy")
            RETURNING "Id", "ChatId", "ReadBy", "FederatedId", "SentAt"
            """, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.Add(new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = messageIds.ToArray()
        });

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var readBy = reader.GetFieldValue<long[]>(2).ToList();
            var message = new Message
            {
                Id = reader.GetInt64(0),
                ChatId = reader.GetGuid(1),
                ReadBy = readBy,
                FederatedId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                SentAt = reader.GetDateTime(4),
            };
            messages.Add(message);
        }
        return messages.OrderBy(message => message.ChatId).ThenBy(message => message.Id).ToList();
    }

    private async Task<List<Message>> MarkTrackedMessagesAsync(
        IReadOnlyCollection<long> messageIds,
        long userId,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return [];
        var messages = await _context.Messages
            .Where(message => messageIds.Contains(message.Id) && !message.IsDeleted)
            .ToListAsync(cancellationToken);
        var changed = messages.Where(message => !message.ReadBy.Contains(userId)).ToList();
        foreach (var message in changed)
            message.ReadBy.Add(userId);
        return changed.OrderBy(message => message.ChatId).ThenBy(message => message.Id).ToList();
    }

    private async Task WriteReadOutboxAsync(
        IReadOnlyCollection<Message> changed,
        long userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (changed.Count == 0) return;
        var entries = new List<MessageOutboxEntry>();
        foreach (var chatMessages in changed.GroupBy(message => message.ChatId))
        {
            var chat = await _context.Chats.AsNoTracking()
                .Where(item => item.Id == chatMessages.Key)
                .Select(item => new { item.IsFederated })
                .FirstAsync(cancellationToken);
            var members = await _context.ChatMembers.AsNoTracking()
                .Where(member => member.ChatId == chatMessages.Key)
                .OrderBy(member => member.UserId)
                .ToListAsync(cancellationToken);
            var localMembers = members.LocalUserIds();
            var remoteParticipants = chat.IsFederated ? members.RemoteParticipants() : [];
            var readerUuid = members.FirstOrDefault(member => member.UserId == userId)?.UserUuid;
            var anchor = chat.IsFederated && remoteParticipants.Count > 0 && readerUuid.HasValue
                ? await _context.Messages.AsNoTracking()
                    .Where(message => message.ChatId == chatMessages.Key && message.FederatedId.HasValue &&
                        message.ReadBy.Contains(userId))
                    .OrderByDescending(message => message.SentAt)
                    .ThenByDescending(message => message.Id)
                    .Select(message => new { message.Id, message.FederatedId })
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            var orderedMessages = chatMessages.OrderBy(message => message.SentAt).ThenBy(message => message.Id).ToList();

            foreach (var (message, index) in orderedMessages.Select((message, index) => (message, index)))
            {
                var isFederatedAnchor = anchor is not null && index == orderedMessages.Count - 1;
                var queueEvent = new MessageReadEvent
                {
                    ChatId = chatMessages.Key,
                    MessageId = message.Id,
                    NewReadBy = message.ReadBy.ToList(),
                    NewReaders = [userId],
                    ChatMembers = localMembers,
                    IsFederated = isFederatedAnchor,
                    ReaderUuid = isFederatedAnchor ? readerUuid : null,
                    UpToFederatedMessageId = isFederatedAnchor ? anchor!.FederatedId : null,
                    RemoteParticipants = isFederatedAnchor ? remoteParticipants : [],
                    LastChangeAt = isFederatedAnchor ? new DateTimeOffset(now, TimeSpan.Zero) : null,
                };
                entries.Add(new MessageOutboxEntry
                {
                    EventId = Guid.NewGuid(),
                    MessageId = message.Id,
                    EventKind = MessageOutboxEventKind.ReadBy,
                    Payload = JsonSerializer.SerializeToUtf8Bytes(queueEvent),
                    TraceParent = Activity.Current?.Id,
                    TraceState = Activity.Current?.TraceStateString,
                    CorrelationId = TraceContext.CorrelationId,
                    CreatedAt = now,
                    NextAttemptAt = now,
                    Status = MessageOutboxStatus.Pending,
                });
            }
        }
        await _context.MessageOutbox.AddRangeAsync(entries, cancellationToken);
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

    private sealed record ReadTransactionCheckpoint(
        Dictionary<object, TrackedEntryCheckpoint> Entries);

    private sealed record TrackedEntryCheckpoint(
        EntityState State,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues CurrentValues,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues OriginalValues,
        Dictionary<string, bool> ModifiedProperties);
}
