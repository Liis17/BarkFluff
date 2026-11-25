using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using Google.Protobuf;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed partial class SqliteApplicationDataStore : IChatListCacheStore
{
    private const int ChatCacheVersion = 1;

    private void InitializeChatCache() => WithChatCache(false, connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_list_cache_version (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chat_list_cache (
                node TEXT NOT NULL,
                user_id INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                position INTEGER NOT NULL,
                protected_data BLOB NOT NULL,
                PRIMARY KEY (node, user_id, kind, position)
            );
            DELETE FROM chat_list_cache WHERE NOT EXISTS (
                SELECT 1 FROM chat_list_cache_version WHERE id = 1 AND version = $version
            );
            INSERT OR REPLACE INTO chat_list_cache_version VALUES (1, $version);
            """;
        command.Parameters.AddWithValue("$version", ChatCacheVersion);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    });

    CachedChatList IChatListCacheStore.Load(ChatListScope scope) => WithChatCache(new CachedChatList([], []), connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = ChatCacheCommand(connection, scope, """
            SELECT kind, position, protected_data FROM chat_list_cache
            WHERE node = $node AND user_id = $user ORDER BY kind, position;
            """, transaction);
        var chats = new List<CachedChat>();
        var folders = new List<ChatFolderData>();
        var damaged = new List<(int Kind, int Position)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var kind = reader.GetInt32(0);
                var position = reader.GetInt32(1);
                try
                {
                    var bytes = ProtectedData.Unprotect(reader.GetFieldValue<byte[]>(2),
                        ChatCacheEntropy(scope, kind), DataProtectionScope.CurrentUser);
                    if (kind == 0 && JsonSerializer.Deserialize<CachedChat>(bytes) is { Id.Length: > 0 } chat &&
                        chat.Definition is not null && Chat.Parser.ParseFrom(chat.Definition).Id == chat.Id)
                        chats.Add(chat);
                    else if (kind == 1 && ChatFolderData.Parser.ParseFrom(bytes) is { FolderId.Length: > 0 } folder)
                        folders.Add(folder);
                    else
                        damaged.Add((kind, position));
                }
                catch (Exception error) when (error is CryptographicException or JsonException or InvalidProtocolBufferException or InvalidCastException)
                {
                    damaged.Add((kind, position));
                }
            }
        }

        foreach (var (kind, position) in damaged)
        {
            using var delete = ChatCacheCommand(connection, scope, """
                DELETE FROM chat_list_cache WHERE node = $node AND user_id = $user
                AND kind = $kind AND position = $position;
                """, transaction);
            delete.Parameters.AddWithValue("$kind", kind);
            delete.Parameters.AddWithValue("$position", position);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return new CachedChatList(chats, folders);
    });

    void IChatListCacheStore.Save(ChatListScope scope, CachedChatList list) => WithChatCache(false, connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var delete = ChatCacheCommand(connection, scope, DeleteChatCache, transaction);
        delete.ExecuteNonQuery();
        using var insert = ChatCacheCommand(connection, scope, """
            INSERT INTO chat_list_cache VALUES ($node, $user, $kind, $position, $data);
            """, transaction);
        var kind = insert.Parameters.Add("$kind", SqliteType.Integer);
        var position = insert.Parameters.Add("$position", SqliteType.Integer);
        var data = insert.Parameters.Add("$data", SqliteType.Blob);
        kind.Value = 0;
        for (var index = 0; index < list.Chats.Count; index++)
            Insert(index, 0, JsonSerializer.SerializeToUtf8Bytes(list.Chats[index]));
        kind.Value = 1;
        for (var index = 0; index < list.Folders.Count; index++)
            Insert(index, 1, list.Folders[index].ToByteArray());

        transaction.Commit();
        return true;

        void Insert(int index, int rowKind, byte[] bytes)
        {
            position.Value = index;
            data.Value = ProtectedData.Protect(bytes, ChatCacheEntropy(scope, rowKind), DataProtectionScope.CurrentUser);
            insert.ExecuteNonQuery();
        }
    });

    private const string DeleteChatCache = "DELETE FROM chat_list_cache WHERE node = $node AND user_id = $user;";

    bool IChatListCacheStore.Clear(ChatListScope scope) => WithChatCache(false, connection =>
    {
        using var command = ChatCacheCommand(connection, scope, DeleteChatCache);
        command.ExecuteNonQuery();
        return true;
    });

    long IChatListCacheStore.Size(ChatListScope scope) => WithChatCache(0L, connection =>
    {
        using var command = ChatCacheCommand(connection, scope, """
            SELECT COALESCE(SUM(length(protected_data)), 0) FROM chat_list_cache
            WHERE node = $node AND user_id = $user;
            """);
        return (long)command.ExecuteScalar()!;
    });

    private T WithChatCache<T>(T fallback, Func<SqliteConnection, T> operation)
    {
        try
        {
            using var connection = OpenDraftConnection();
            return operation(connection);
        }
        catch (Exception error) when (error is SqliteException or CryptographicException)
        {
            return fallback;
        }
    }

    private static SqliteCommand ChatCacheCommand(SqliteConnection connection, ChatListScope scope, string sql,
        SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$node", scope.Node);
        command.Parameters.AddWithValue("$user", scope.UserId);
        return command;
    }

    // Дополнительная entropy привязывает шифротекст к версии, паре нода и пользователь и типу записи.
    private static byte[] ChatCacheEntropy(ChatListScope scope, int kind) =>
        Encoding.UTF8.GetBytes(FormattableString.Invariant($"BarkFluff.WinUI.chat-list.v{ChatCacheVersion}:{scope.Node}:{scope.UserId}:{kind}"));
}
