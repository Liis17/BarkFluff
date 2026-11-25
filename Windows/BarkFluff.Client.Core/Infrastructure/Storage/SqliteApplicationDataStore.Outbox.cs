using System.Security.Cryptography;
using System.Text.Json;
using BarkFluff.Client.Core.Services;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed partial class SqliteApplicationDataStore : IOutgoingMessageStore
{
    IReadOnlyList<OutgoingMessage> IOutgoingMessageStore.Load(OutgoingScope scope)
    {
        using var connection = OpenDraftConnection();
        using var command = OutgoingCommand(connection, scope, """
            SELECT sequence, operation_id, protected_data FROM outgoing_messages
            WHERE node = $node AND user_id = $user ORDER BY sequence;
            """);
        var messages = new List<OutgoingMessage>();
        var corrupt = new List<long>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                try
                {
                    if (!Guid.TryParse(reader.GetString(1), out var id) || reader.GetValue(2) is not byte[] blob)
                        throw new JsonException("Invalid outgoing row");
                    var bytes = ProtectedData.Unprotect(blob, OutgoingEntropy(scope, id), DataProtectionScope.CurrentUser);
                    var message = JsonSerializer.Deserialize<OutgoingMessage>(bytes);
                    if (message is null || message.Id != id || string.IsNullOrEmpty(message.ChatId) ||
                        message.Text is null || message.Forwards is null || message.Attachments is null ||
                        message.Files is null || message.Uploaded is null || message.Uploaded.Length > message.Files.Length ||
                        message.Files.Any(file => file is null || file.Extension is null || file.Name is null))
                        throw new JsonException("Invalid outgoing message");
                    messages.Add(message);
                }
                catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
                {
                    corrupt.Add(reader.GetInt64(0));
                }
            }
        }

        using var delete = OutgoingCommand(connection, scope,
            "DELETE FROM outgoing_messages WHERE node = $node AND user_id = $user AND sequence = $sequence;");
        var sequence = delete.Parameters.Add("$sequence", SqliteType.Integer);
        foreach (var value in corrupt)
        {
            sequence.Value = value;
            delete.ExecuteNonQuery();
        }
        return messages;
    }

    IReadOnlyList<(OutgoingScope Scope, Guid Id)> IOutgoingMessageStore.ListOperations()
    {
        using var connection = OpenDraftConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT node, user_id, operation_id FROM outgoing_messages;";
        using var reader = command.ExecuteReader();
        var operations = new List<(OutgoingScope, Guid)>();
        while (reader.Read())
            if (Guid.TryParse(reader.GetString(2), out var id))
                operations.Add((new(reader.GetString(0), reader.GetInt64(1)), id));
        return operations;
    }

    void IOutgoingMessageStore.Save(OutgoingScope scope, OutgoingMessage message)
    {
        using var connection = OpenDraftConnection();
        SaveOutgoing(connection, null, scope, message);
    }

    bool IOutgoingMessageStore.TrySaveNotificationReply(OutgoingScope scope, OutgoingMessage message)
    {
        using var connection = OpenDraftConnection();
        using var transaction = connection.BeginTransaction();
        using var receipt = OutgoingCommand(connection, scope, """
            INSERT INTO notification_replies (node, user_id, operation_id)
            VALUES ($node, $user, $id)
            ON CONFLICT DO NOTHING;
            """);
        receipt.Transaction = transaction;
        receipt.Parameters.AddWithValue("$id", message.Id.ToString());
        if (receipt.ExecuteNonQuery() == 0)
            return false;
        SaveOutgoing(connection, transaction, scope, message);
        transaction.Commit();
        return true;
    }

    private static void SaveOutgoing(SqliteConnection connection, SqliteTransaction? transaction,
        OutgoingScope scope, OutgoingMessage message)
    {
        using var command = OutgoingCommand(connection, scope, """
            INSERT INTO outgoing_messages (node, user_id, operation_id, protected_data)
            VALUES ($node, $user, $id, $data)
            ON CONFLICT(node, user_id, operation_id) DO UPDATE SET protected_data = excluded.protected_data;
            """);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", message.Id.ToString());
        command.Parameters.AddWithValue("$data", ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(message),
            OutgoingEntropy(scope, message.Id), DataProtectionScope.CurrentUser));
        command.ExecuteNonQuery();
    }

    void IOutgoingMessageStore.Remove(OutgoingScope scope, Guid id)
    {
        using var connection = OpenDraftConnection();
        using var command = OutgoingCommand(connection, scope,
            "DELETE FROM outgoing_messages WHERE node = $node AND user_id = $user AND operation_id = $id;");
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    void IOutgoingMessageStore.Clear(OutgoingScope scope)
    {
        using var connection = OpenDraftConnection();
        using var transaction = connection.BeginTransaction();
        using var command = OutgoingCommand(connection, scope, """
            DELETE FROM outgoing_messages WHERE node = $node AND user_id = $user;
            DELETE FROM notification_replies WHERE node = $node AND user_id = $user;
            """);
        command.Transaction = transaction;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static SqliteCommand OutgoingCommand(SqliteConnection connection, OutgoingScope scope, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$node", scope.Node);
        command.Parameters.AddWithValue("$user", scope.UserId);
        return command;
    }

    private static byte[] OutgoingEntropy(OutgoingScope scope, Guid id) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Purpose = "BarkFluff.WinUI.outbox.v1", scope.Node, scope.UserId, id });
}

