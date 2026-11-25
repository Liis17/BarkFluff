using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarkFluff.Client.Core.Services.SecretChats;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed partial class SqliteApplicationDataStore
{
    internal readonly record struct SecretScope(string Node, long UserId)
    {
        internal static SecretScope Create(string node, long userId) => new(NormalizeDraftNode(node), userId);
    }

    internal sealed class SecretScopeLock
    {
        internal SemaphoreSlim Gate { get; } = new(1);
        internal volatile int Epoch;
    }

    internal sealed record SecretSnapshot(byte[] State, ulong Generation, string Revision)
    {
        internal static SecretSnapshot Empty { get; } = new([], 0, "");
    }

    internal enum SecretTable { Chats, Replay, History, Counters, FailedReplay }

    private static readonly ConcurrentDictionary<(string, SecretScope), SecretScopeLock> SecretLocks = new();

    internal SecretScopeLock GetSecretScopeLock(SecretScope scope) =>
        SecretLocks.GetOrAdd((Path.GetFullPath(_paths.DatabasePath).ToUpperInvariant(), scope), _ => new());

    private static string TableName(SecretTable table) => table switch
    {
        SecretTable.Chats => "secret_chats",
        SecretTable.Replay => "secret_replay",
        SecretTable.History => "secret_history",
        SecretTable.Counters => "secret_counters",
        SecretTable.FailedReplay => "secret_failed_replay",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };

    private static async Task InitializeSecretChatsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS secret_snapshot (
                node TEXT NOT NULL, user_id INTEGER NOT NULL, generation INTEGER NOT NULL,
                revision TEXT NOT NULL, protected_data BLOB NOT NULL, PRIMARY KEY (node, user_id)
            );
            """;
        foreach (var table in Enum.GetValues<SecretTable>())
            command.CommandText += $"""
                CREATE TABLE IF NOT EXISTS {TableName(table)} (
                    node TEXT NOT NULL, user_id INTEGER NOT NULL, record_id TEXT NOT NULL,
                    protected_data BLOB NOT NULL, PRIMARY KEY (node, user_id, record_id)
                );
                """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal SecretSnapshot ReadSecretSnapshot(SecretScope scope)
    {
        using var connection = OpenDraftConnection();
        using var command = SecretCommand(connection, scope, """
            SELECT generation, revision, protected_data FROM secret_snapshot WHERE node = $node AND user_id = $user;
            """);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(Unprotect(reader, 2, scope, "snapshot", reader.GetString(1)), checked((ulong)reader.GetInt64(0)), reader.GetString(1))
            : SecretSnapshot.Empty;
    }

    internal T? ReadSecret<T>(SecretScope scope, SecretTable table, string id) where T : class =>
        ReadSecrets<T>(scope, table, id).SingleOrDefault();

    internal IReadOnlyList<T> ReadSecrets<T>(SecretScope scope, SecretTable table, string? id = null) where T : class
    {
        using var connection = OpenDraftConnection();
        string kind = TableName(table);
        using var command = SecretCommand(connection, scope,
            $"SELECT record_id, protected_data FROM {kind} WHERE node = $node AND user_id = $user");
        if (id is not null)
        {
            command.CommandText += " AND record_id = $id";
            command.Parameters.AddWithValue("$id", RecordId(id));
        }

        using var reader = command.ExecuteReader();
        var entries = new List<T>();
        while (reader.Read())
        {
            var json = Unprotect(reader, 1, scope, kind, reader.GetString(0));
            try
            {
                entries.Add(JsonSerializer.Deserialize<T>(json) ?? throw new JsonException());
            }
            catch (JsonException)
            {
                throw new InvalidDataException("Secret chat storage contains an invalid record.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
        return entries;
    }

    internal SecretSnapshot CommitSecret(SecretScope scope, SecretSnapshot expected, byte[] state, ulong generation,
        Action<SecretWriter> write)
    {
        using var connection = OpenSecretWriteConnection();
        using var transaction = connection.BeginTransaction();
        var revision = Guid.NewGuid().ToString("N");
        using var command = SecretCommand(connection, scope, expected.Revision.Length == 0 ? """
            INSERT INTO secret_snapshot (node, user_id, generation, revision, protected_data)
            VALUES ($node, $user, $generation, $revision, $data)
            ON CONFLICT (node, user_id) DO NOTHING;
            """ : """
            UPDATE secret_snapshot SET generation = $generation, revision = $revision, protected_data = $data
            WHERE node = $node AND user_id = $user AND generation = $expected AND revision = $previous;
            """, transaction);
        command.Parameters.AddWithValue("$generation", checked((long)generation));
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$data", Protect(state, scope, "snapshot", revision));
        if (expected.Revision.Length != 0)
        {
            command.Parameters.AddWithValue("$expected", checked((long)expected.Generation));
            command.Parameters.AddWithValue("$previous", expected.Revision);
        }
        if (command.ExecuteNonQuery() != 1)
            throw new SignalProtocolException(SignalError.GenerationMismatch);

        write(new SecretWriter(connection, transaction, scope));
        transaction.Commit();
        return new(state, generation, revision);
    }

    internal async Task ForgetSecretChatsAsync(SecretScope scope, int? expectedEpoch = null)
    {
        var scopeLock = GetSecretScopeLock(scope);
        await scopeLock.Gate.WaitAsync();
        try
        {
            if (expectedEpoch is not null && expectedEpoch != scopeLock.Epoch)
                return;
            scopeLock.Epoch++;
            using var connection = OpenSecretWriteConnection();
            using var transaction = connection.BeginTransaction();
            using var command = SecretCommand(connection, scope,
                "DELETE FROM secret_snapshot WHERE node = $node AND user_id = $user;", transaction);
            foreach (var table in Enum.GetValues<SecretTable>())
                command.CommandText += $"DELETE FROM {TableName(table)} WHERE node = $node AND user_id = $user;";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        finally
        {
            scopeLock.Gate.Release();
        }
    }

    internal sealed class SecretWriter(SqliteConnection connection, SqliteTransaction transaction, SecretScope scope)
    {
        internal void Set<T>(SecretTable table, string id, T? value) where T : class
        {
            var kind = TableName(table);
            var key = RecordId(id);
            using var command = SecretCommand(connection, scope, value is null
                ? $"DELETE FROM {kind} WHERE node = $node AND user_id = $user AND record_id = $id;"
                : $"""
                    INSERT INTO {kind} (node, user_id, record_id, protected_data) VALUES ($node, $user, $id, $data)
                    ON CONFLICT (node, user_id, record_id) DO UPDATE SET protected_data = excluded.protected_data;
                    """, transaction);
            command.Parameters.AddWithValue("$id", key);
            if (value is not null)
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(value);
                try
                {
                    command.Parameters.AddWithValue("$data", Protect(json, scope, kind, key));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(json);
                }
            }
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenSecretWriteConnection()
    {
        var connection = OpenDraftConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous = FULL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand SecretCommand(SqliteConnection connection, SecretScope scope, string sql,
        SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$node", scope.Node);
        command.Parameters.AddWithValue("$user", scope.UserId);
        return command;
    }

    private static string RecordId(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static byte[] SecretEntropy(SecretScope scope, string kind, string id) =>
        JsonSerializer.SerializeToUtf8Bytes(new object[] { "BarkFluff.WinUI.secret.v1", scope.Node, scope.UserId, kind, id });

    private static byte[] Protect(byte[] data, SecretScope scope, string kind, string id) =>
        ProtectedData.Protect(data, SecretEntropy(scope, kind, id), DataProtectionScope.CurrentUser);

    private static byte[] Unprotect(SqliteDataReader reader, int ordinal, SecretScope scope, string kind, string id)
    {
        try
        {
            return ProtectedData.Unprotect(reader.GetFieldValue<byte[]>(ordinal), SecretEntropy(scope, kind, id),
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("Secret chat storage could not be decrypted.");
        }
    }
}
