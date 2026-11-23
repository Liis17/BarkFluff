using System.Security.Cryptography;
using System.Text.Json;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed partial class SqliteApplicationDataStore : IChatDraftStore
{
    public Dictionary<string, ChatDraft> Load(string node, long userId)
    {
        try
        {
            using var connection = OpenDraftConnection();
            return ReadDrafts(connection, NormalizeDraftNode(node), userId);
        }
        catch (SqliteException)
        {
            return [];
        }
    }

    private static Dictionary<string, ChatDraft> ReadDrafts(
        SqliteConnection connection, string node, long userId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT chat_id, protected_data FROM chat_drafts WHERE node = $node AND user_id = $user;";
        command.Parameters.AddWithValue("$node", node);
        command.Parameters.AddWithValue("$user", userId);
        using var reader = command.ExecuteReader();
        var drafts = new Dictionary<string, ChatDraft>();
        while (reader.Read())
        {
            var chatId = reader.GetString(0);
            try
            {
                var json = ProtectedData.Unprotect(reader.GetFieldValue<byte[]>(1),
                    DraftEntropy(node, userId, chatId), DataProtectionScope.CurrentUser);
                if (JsonSerializer.Deserialize<ChatDraft>(json) is { } draft)
                    drafts.Add(chatId, draft);
            }
            catch (CryptographicException)
            {
                // Повреждённая запись не мешает восстановлению остальных черновиков.
            }
            catch (JsonException)
            {
                // Запись с неизвестным форматом остаётся недоступной.
            }
        }
        return drafts;
    }

    public void Save(string node, long userId, string chatId, ChatDraft draft)
    {
        try
        {
            using var connection = OpenDraftConnection();
            WriteDraft(connection, NormalizeDraftNode(node), userId, chatId, draft);
        }
        catch (Exception exception) when (exception is SqliteException or CryptographicException)
        {
            // При недоступном диске или DPAPI черновик остаётся в памяти сессии.
        }
    }

    public void RemoveNonRegularDrafts(string node, long userId)
    {
        node = NormalizeDraftNode(node);
        using var connection = OpenDraftConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var (chatId, draft) in ReadDrafts(connection, node, userId, transaction))
            if (draft.Type != ChatType.Regular)
                WriteDraft(connection, node, userId, chatId, draft with { Deleted = true, Pending = false, LocalOnly = false },
                    transaction);
        transaction.Commit();
    }

    private SqliteConnection OpenDraftConnection()
    {
        var connection = new SqliteConnection(_connectionString) { DefaultTimeout = 1 };
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void WriteDraft(SqliteConnection connection, string node, long userId, string chatId,
        ChatDraft draft, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$node", node);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$chat", chatId);
        if (draft.HasContent || draft.Pending || draft.LocalOnly)
        {
            command.CommandText = """
                INSERT INTO chat_drafts (node, user_id, chat_id, protected_data)
                VALUES ($node, $user, $chat, $data)
                ON CONFLICT(node, user_id, chat_id) DO UPDATE SET protected_data = excluded.protected_data;
                """;
            command.Parameters.AddWithValue("$data", ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(draft),
                DraftEntropy(node, userId, chatId), DataProtectionScope.CurrentUser));
        }
        else
        {
            command.CommandText = "DELETE FROM chat_drafts WHERE node = $node AND user_id = $user AND chat_id = $chat;";
        }
        command.ExecuteNonQuery();
    }

    private static string NormalizeDraftNode(string node) => node.Trim().TrimEnd('/').ToUpperInvariant();

    private static byte[] DraftEntropy(string node, long userId, string chatId) =>
        JsonSerializer.SerializeToUtf8Bytes(new object[] { "BarkFluff.WinUI.chat-draft.v1", node, userId, chatId });
}
