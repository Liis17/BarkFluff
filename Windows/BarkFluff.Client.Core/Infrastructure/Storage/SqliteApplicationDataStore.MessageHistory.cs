using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Google.Protobuf;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed partial class SqliteApplicationDataStore : IMessageHistoryCacheStore
{
    private const int HistoryCacheVersion = 1;
    internal const int HistoryMessagesPerChat = 1000;
    internal const int HistoryMessagesPerScope = 20_000;
    private const string DeleteHistory = "DELETE FROM message_history_cache WHERE node = $node AND user_id = $user";
    private const string DeletePins = "DELETE FROM message_history_pins WHERE node = $node AND user_id = $user";
    private const string HistoryChat = "node = $node AND user_id = $user AND chat_id = $chat";

    private void InitializeMessageHistory() => WithChatCache(false, connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS message_history_version (
                id INTEGER PRIMARY KEY CHECK (id = 1), version INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS message_history_cache (
                node TEXT NOT NULL, user_id INTEGER NOT NULL, chat_id TEXT NOT NULL,
                message_id INTEGER NOT NULL, sent_at INTEGER NOT NULL, edited_at INTEGER NOT NULL,
                segment TEXT NOT NULL, protected_data BLOB NOT NULL,
                PRIMARY KEY (node, user_id, chat_id, message_id)
            );
            CREATE INDEX IF NOT EXISTS message_history_chat_order
                ON message_history_cache (node, user_id, chat_id, sent_at DESC, message_id DESC);
            CREATE INDEX IF NOT EXISTS message_history_scope_order
                ON message_history_cache (node, user_id, sent_at DESC, message_id DESC);
            CREATE TABLE IF NOT EXISTS message_history_pins (
                node TEXT NOT NULL, user_id INTEGER NOT NULL, chat_id TEXT NOT NULL, protected_data BLOB NOT NULL,
                PRIMARY KEY (node, user_id, chat_id)
            );
            DELETE FROM message_history_cache WHERE NOT EXISTS (
                SELECT 1 FROM message_history_version WHERE id = 1 AND version = $version
            );
            DELETE FROM message_history_pins WHERE NOT EXISTS (
                SELECT 1 FROM message_history_version WHERE id = 1 AND version = $version
            );
            INSERT OR REPLACE INTO message_history_version VALUES (1, $version);
            """;
        command.Parameters.AddWithValue("$version", HistoryCacheVersion);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    });

    CachedHistoryPage IMessageHistoryCacheStore.Read(ChatListScope scope, string chatId, bool isPrivate, long anchor,
        int pageSize, bool older) =>
        WithChatCache(new CachedHistoryPage([], false), connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = HistoryCommand(transaction, scope, chatId, $"""
                WITH target AS (
                    SELECT message_id, sent_at, segment FROM message_history_cache
                    WHERE {HistoryChat} AND message_id = $anchor
                ), before AS (
                    SELECT message_id, sent_at, segment, protected_data FROM message_history_cache
                    WHERE {HistoryChat} AND (
                        ($anchor = 0 AND segment = '') OR (
                            segment = (SELECT segment FROM target)
                            AND (sent_at, message_id) <= (SELECT sent_at, message_id FROM target)
                        )
                    )
                    ORDER BY sent_at DESC, message_id DESC LIMIT $before
                ), after AS (
                    SELECT message_id, sent_at, segment, protected_data FROM message_history_cache
                    WHERE {HistoryChat} AND segment = (SELECT segment FROM target)
                    AND (sent_at, message_id) > (SELECT sent_at, message_id FROM target)
                    ORDER BY sent_at, message_id LIMIT $after
                )
                SELECT * FROM before UNION ALL SELECT * FROM after ORDER BY sent_at, message_id;
                """);
            command.Parameters.AddWithValue("$anchor", anchor);
            command.Parameters.AddWithValue("$before", anchor == 0 ? pageSize : pageSize + 1);
            command.Parameters.AddWithValue("$after", anchor == 0 || older ? 0 : pageSize);
            var groups = new List<List<CachedHistoryMessage>> { new() };
            var damaged = new List<(long Time, long Id)>();
            var segment = "";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var time = reader.GetInt64(1);
                    segment = reader.GetString(2);
                    if (DecodeHistoryMessage(scope, chatId, isPrivate, id, time, reader.GetValue(3)) is { } row)
                    {
                        groups[^1].Add(row);
                        continue;
                    }
                    damaged.Add((time, id));
                    groups.Add([]);
                }
            }

            foreach (var (time, id) in damaged)
            {
                // После потери строки смежность по обе стороны неизвестна.
                using var split = HistoryCommand(transaction, scope, chatId, $"""
                    UPDATE message_history_cache SET segment = $detached WHERE {HistoryChat}
                      AND segment = $segment AND (sent_at, message_id) < ($time, $id);
                    DELETE FROM message_history_cache WHERE {HistoryChat} AND message_id = $id;
                    """);
                split.Parameters.AddWithValue("$detached", Guid.NewGuid().ToString("N"));
                split.Parameters.AddWithValue("$segment", segment);
                split.Parameters.AddWithValue("$time", time);
                split.Parameters.AddWithValue("$id", id);
                split.ExecuteNonQuery();
            }
            var pins = anchor == 0 && !isPrivate ? ReadHistoryPins(transaction, scope, chatId) : null;
            var selected = anchor == 0 ? groups[^1] : groups.Find(group => group.Any(row => row.Id == anchor));
            var includesTail = segment.Length == 0 && selected == groups[^1];
            var hasOlder = false;
            if (selected is { Count: > 0 })
            {
                // Проверяем границы после удаления повреждённых строк, не читая их содержимое.
                using var edges = HistoryCommand(transaction, scope, chatId, $"""
                    SELECT EXISTS (
                        SELECT 1 FROM message_history_cache WHERE {HistoryChat} AND segment = (
                            SELECT segment FROM message_history_cache WHERE {HistoryChat} AND message_id = $first
                        ) AND (sent_at, message_id) < ($firstTime, $first)
                    ), EXISTS (
                        SELECT 1 FROM message_history_cache WHERE {HistoryChat} AND segment = ''
                        AND (sent_at, message_id) > ($lastTime, $last)
                    );
                    """);
                edges.Parameters.AddWithValue("$first", selected[0].Id);
                edges.Parameters.AddWithValue("$firstTime", selected[0].SentAt.UtcTicks);
                edges.Parameters.AddWithValue("$last", selected[^1].Id);
                edges.Parameters.AddWithValue("$lastTime", selected[^1].SentAt.UtcTicks);
                using var reader = edges.ExecuteReader();
                reader.Read();
                hasOlder = reader.GetBoolean(0);
                includesTail &= !reader.GetBoolean(1);
            }
            transaction.Commit();
            return new CachedHistoryPage(selected ?? [], includesTail, pins, hasOlder);
        });

    private static CachedHistoryMessage? DecodeHistoryMessage(ChatListScope scope, string chatId, bool isPrivate,
        long id, long time, object data)
    {
        try
        {
            var bytes = ProtectedData.Unprotect((byte[])data, HistoryEntropy(scope, chatId, id, isPrivate),
                DataProtectionScope.CurrentUser);
            var sentAt = new DateTimeOffset(time, TimeSpan.Zero);
            var row = isPrivate
                ? new CachedHistoryMessage(id, sentAt, null, EncryptedMessage.Parser.ParseFrom(bytes))
                : new CachedHistoryMessage(id, sentAt, JsonSerializer.Deserialize<MessageModel>(bytes), null);
            if (row.Regular is { SentAt: not null, Attachments: not null, ReadBy: not null } regular &&
                regular.MessageId == id && regular.ChatId == chatId && regular.SentAt.ToDateTimeOffset() == sentAt ||
                row.Encrypted is { IsDeleted: false, SentAt: not null } encrypted && encrypted.Id == id &&
                encrypted.ChatId == chatId && encrypted.SentAt.ToDateTimeOffset() == sentAt)
                return row;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or InvalidProtocolBufferException
            or InvalidCastException or InvalidOperationException or ArgumentOutOfRangeException)
        {
        }
        return null;
    }

    void IMessageHistoryCacheStore.Write(ChatListScope scope, IReadOnlyList<HistoryCacheWrite> writes) =>
        WithChatCache(false, connection =>
        {
            using var transaction = connection.BeginTransaction();
            foreach (var write in writes)
            {
                if (write.Pins is { } pins)
                    WriteHistoryPins(transaction, scope, write.ChatId, pins);
                else if (write.PinChange is { } pinChange)
                    WriteHistoryPinChange(transaction, scope, pinChange);
                else if (write.Range is { } range)
                    WriteHistoryRange(transaction, scope, write, range);
                else
                    WriteHistoryEvent(transaction, scope, write);
            }
            foreach (var chatId in writes.Where(write => write.Messages.Count != 0)
                .Select(write => write.ChatId).Distinct())
            {
                using var trim = HistoryCommand(transaction, scope, chatId, $"""
                    DELETE FROM message_history_cache WHERE rowid IN (
                        SELECT rowid FROM message_history_cache WHERE {HistoryChat}
                        ORDER BY sent_at DESC, message_id DESC LIMIT -1 OFFSET {HistoryMessagesPerChat}
                    );
                    """);
                trim.ExecuteNonQuery();
            }
            using var total = ChatCacheCommand(connection, scope, $"""
                DELETE FROM message_history_cache WHERE rowid IN (
                    SELECT rowid FROM message_history_cache WHERE node = $node AND user_id = $user
                    ORDER BY sent_at DESC, message_id DESC LIMIT -1 OFFSET {HistoryMessagesPerScope}
                );
                DELETE FROM message_history_pins WHERE rowid IN (
                    SELECT rowid FROM message_history_pins WHERE node = $node AND user_id = $user
                    ORDER BY rowid DESC LIMIT -1 OFFSET {HistoryMessagesPerChat}
                );
                """, transaction);
            total.ExecuteNonQuery();
            transaction.Commit();
            return true;
        });

    private static void WriteHistoryRange(SqliteTransaction transaction, ChatListScope scope, HistoryCacheWrite write,
        ((DateTimeOffset Time, long Id) Start, (DateTimeOffset Time, long Id) End) range)
    {
        var ids = write.Messages.Select(row => row.Id).ToHashSet();
        var segments = new HashSet<string>();
        var missing = new List<long>();
        using (var query = HistoryCommand(transaction, scope, write.ChatId, $"""
            SELECT message_id, sent_at, segment FROM message_history_cache WHERE {HistoryChat};
            """))
        using (var reader = query.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var position = (reader.GetInt64(1), id);
                if (ids.Contains(id))
                    segments.Add(reader.GetString(2));
                else if (position.CompareTo((range.Start.Time.UtcTicks, range.Start.Id)) >= 0 &&
                         position.CompareTo((range.End.Time.UtcTicks, range.End.Id)) <= 0)
                    missing.Add(id);
            }
        }

        var tail = range.End == (DateTimeOffset.MaxValue, long.MaxValue);
        if (tail && !segments.Contains(""))
        {
            using var delete = HistoryCommand(transaction, scope, write.ChatId, $"""
                DELETE FROM message_history_cache WHERE {HistoryChat} AND segment = '';
                """);
            delete.ExecuteNonQuery();
        }
        foreach (var id in missing)
            DeleteHistoryMessage(transaction, scope, write.ChatId, id);

        var segment = tail || segments.Contains("") ? "" : segments.FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        foreach (var merged in segments.Where(old => old != segment))
        {
            using var move = HistoryCommand(transaction, scope, write.ChatId, $"""
                UPDATE message_history_cache SET segment = $segment WHERE {HistoryChat} AND segment = $old;
                """);
            move.Parameters.AddWithValue("$segment", segment);
            move.Parameters.AddWithValue("$old", merged);
            move.ExecuteNonQuery();
        }
        foreach (var row in write.Messages)
            SaveHistoryMessage(transaction, scope, write.ChatId, segment, row);
    }

    private static void WriteHistoryEvent(SqliteTransaction transaction, ChatListScope scope, HistoryCacheWrite write)
    {
        if (write.DeleteId is { } id)
        {
            DeleteHistoryMessage(transaction, scope, write.ChatId, id);
            if (ReadHistoryPins(transaction, scope, write.ChatId) is { } pins && pins.Any(pin => pin.Message.Id == id))
                WriteHistoryPins(transaction, scope, write.ChatId, pins.Where(pin => pin.Message.Id != id));
            return;
        }

        foreach (var row in write.Messages)
        {
            using var query = HistoryCommand(transaction, scope, write.ChatId, $"""
                SELECT segment FROM message_history_cache WHERE {HistoryChat} AND message_id = $id
                UNION ALL
                SELECT '' WHERE $append AND (
                    NOT EXISTS (SELECT 1 FROM message_history_cache WHERE {HistoryChat} AND segment = '')
                    OR EXISTS (
                        SELECT 1 FROM message_history_cache WHERE {HistoryChat} AND segment = ''
                        AND (sent_at, message_id) <= ($time, $id)
                    )
                ) LIMIT 1;
                """);
            query.Parameters.AddWithValue("$id", row.Id);
            query.Parameters.AddWithValue("$append", write.Append);
            query.Parameters.AddWithValue("$time", row.SentAt.UtcTicks);
            var segment = query.ExecuteScalar() as string;
            if (segment is not null)
                SaveHistoryMessage(transaction, scope, write.ChatId, segment, row);
        }
    }

    private static void SaveHistoryMessage(SqliteTransaction transaction, ChatListScope scope, string chatId,
        string segment, CachedHistoryMessage row)
    {
        var isPrivate = row.Encrypted is not null;
        if (row.Id <= 0 || (isPrivate ? row.Encrypted!.ChatId != chatId || row.Encrypted.IsDeleted : row.Regular?.ChatId != chatId))
            return;

        var bytes = isPrivate ? row.Encrypted!.ToByteArray() : JsonSerializer.SerializeToUtf8Bytes(row.Regular);
        using var command = HistoryCommand(transaction, scope, chatId, """
            INSERT INTO message_history_cache VALUES ($node, $user, $chat, $id, $time, $edit, $segment, $data)
            ON CONFLICT (node, user_id, chat_id, message_id) DO UPDATE SET
                sent_at = excluded.sent_at, edited_at = excluded.edited_at, protected_data = excluded.protected_data
            WHERE excluded.edited_at >= message_history_cache.edited_at;
            """);
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$time", row.SentAt.UtcTicks);
        command.Parameters.AddWithValue("$edit", row.EditTime);
        command.Parameters.AddWithValue("$segment", segment);
        command.Parameters.AddWithValue("$data", ProtectedData.Protect(bytes,
            HistoryEntropy(scope, chatId, row.Id, isPrivate), DataProtectionScope.CurrentUser));
        command.ExecuteNonQuery();
    }

    private static void DeleteHistoryMessage(SqliteTransaction transaction, ChatListScope scope, string chatId, long id)
    {
        using var command = HistoryCommand(transaction, scope, chatId, $"""
            DELETE FROM message_history_cache WHERE {HistoryChat} AND message_id = $id;
            """);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    bool IMessageHistoryCacheStore.Clear(ChatListScope scope) => WithChatCache(false, connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = ChatCacheCommand(connection, scope, $"{DeleteHistory}; {DeletePins};", transaction);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    });

    long IMessageHistoryCacheStore.Size(ChatListScope scope) => WithChatCache(0L, connection =>
    {
        using var command = ChatCacheCommand(connection, scope, """
            SELECT (SELECT COALESCE(SUM(length(protected_data)), 0) FROM message_history_cache
                    WHERE node = $node AND user_id = $user) +
                   (SELECT COALESCE(SUM(length(protected_data)), 0) FROM message_history_pins
                    WHERE node = $node AND user_id = $user);
            """);
        return (long)command.ExecuteScalar()!;
    });

    private static IReadOnlyList<PinnedMessageInfo>? ReadHistoryPins(SqliteTransaction transaction, ChatListScope scope, string chatId)
    {
        using var query = HistoryCommand(transaction, scope, chatId,
            $"SELECT protected_data FROM message_history_pins WHERE {HistoryChat};");
        if (query.ExecuteScalar() is not byte[] blob)
            return null;
        try
        {
            var bytes = ProtectedData.Unprotect(blob, HistoryEntropy(scope, chatId, 0, false), DataProtectionScope.CurrentUser);
            var pins = ListPinnedMessagesResponse.Parser.ParseFrom(bytes).Pinned;
            if (pins.All(HasHistoryPinData))
                return pins.ToArray();
        }
        catch (Exception error) when (error is CryptographicException or InvalidProtocolBufferException)
        {
        }
        WriteHistoryPins(transaction, scope, chatId, []);
        return null;
    }

    private static bool HasHistoryPinData(PinnedMessageInfo pin) =>
        pin.Message is { Id: > 0, SentAt: not null, Content: not null };

    private static void WriteHistoryPins(SqliteTransaction transaction, ChatListScope scope, string chatId,
        IEnumerable<PinnedMessageInfo> pins)
    {
        var response = new ListPinnedMessagesResponse();
        response.Pinned.AddRange(pins);
        if (response.Pinned.Count == 0 || !response.Pinned.All(HasHistoryPinData))
        {
            using var delete = HistoryCommand(transaction, scope, chatId, $"{DeletePins} AND chat_id = $chat;");
            delete.ExecuteNonQuery();
            return;
        }
        using var command = HistoryCommand(transaction, scope, chatId, """
            INSERT OR REPLACE INTO message_history_pins VALUES ($node, $user, $chat, $data);
            """);
        command.Parameters.AddWithValue("$data", ProtectedData.Protect(response.ToByteArray(),
            HistoryEntropy(scope, chatId, 0, false), DataProtectionScope.CurrentUser));
        command.ExecuteNonQuery();
    }

    private static void WriteHistoryPinChange(SqliteTransaction transaction, ChatListScope scope, MessageUpdate update)
    {
        var pins = ReadHistoryPins(transaction, scope, update.ChatId) ?? [];
        var remaining = pins.Where(pin => pin.Message.Id != update.MessageId);
        if (update.Kind == MessageUpdateKind.Pin)
        {
            var old = pins.FirstOrDefault(pin => pin.Message.Id == update.MessageId);
            if (old is not null && Comparer<Google.Protobuf.WellKnownTypes.Timestamp?>.Default
                .Compare(old.PinnedAt, update.PinnedAt) >= 0)
                return;
            var message = old?.Message ?? ReadMessageForHistoryPin(transaction, scope, update.ChatId, update.MessageId);
            if (message is null)
            {
                WriteHistoryPins(transaction, scope, update.ChatId, []);
                return;
            }
            remaining = remaining.Prepend(new PinnedMessageInfo
            {
                Message = message,
                PinnedAt = update.PinnedAt,
                PinnerUserId = update.PinnerId
            });
        }
        WriteHistoryPins(transaction, scope, update.ChatId,
            update.Kind == MessageUpdateKind.UnpinAll ? [] : remaining.Take(HistoryMessagesPerChat));
    }

    private static Message? ReadMessageForHistoryPin(SqliteTransaction transaction, ChatListScope scope,
        string chatId, long id)
    {
        using var query = HistoryCommand(transaction, scope, chatId,
            $"SELECT sent_at, protected_data FROM message_history_cache WHERE {HistoryChat} AND message_id = $id;");
        query.Parameters.AddWithValue("$id", id);
        using var reader = query.ExecuteReader();
        if (!reader.Read() ||
            DecodeHistoryMessage(scope, chatId, false, id, reader.GetInt64(0), reader.GetValue(1))?.Regular is not { } row)
            return null;

        return new Message
        {
            Id = row.MessageId,
            SenderId = row.SenderId,
            SentAt = row.SentAt,
            Type = row.Type,
            IsEdited = row.IsEdited,
            EditedAt = row.EditedAt,
            Content = new MessageContent { Text = row.Text }
        };
    }

    private static SqliteCommand HistoryCommand(SqliteTransaction transaction, ChatListScope scope, string chatId, string sql)
    {
        var command = ChatCacheCommand(transaction.Connection!, scope, sql, transaction);
        command.Parameters.AddWithValue("$chat", chatId);
        return command;
    }

    private static byte[] HistoryEntropy(ChatListScope scope, string chatId, long id, bool isPrivate) =>
        Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"BarkFluff.WinUI.history.v{HistoryCacheVersion}:{scope.Node.Length}:{scope.Node}:{scope.UserId}:{chatId.Length}:{chatId}:{id}:{isPrivate}"));
}

