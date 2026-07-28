using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;

using Microsoft.Data.Sqlite;

using System.IO;
using System.Text.Json;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Storage;

public sealed class SqliteApplicationDataStore : IApplicationDataStore
{
    private const string WelcomeSeenKey = "onboarding.welcome-seen";
    private const string LanguageKey = "application.language";
    private const string ThemeKey = "application.theme";

    private readonly AppDataPaths _paths;
    private readonly string _connectionString;

    public SqliteApplicationDataStore(AppDataPaths paths)
    {
        _paths = paths;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS selected_node (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                beacon_address TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS selected_node_services (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                configuration_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS secure_session (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                protected_data BLOB NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> HasSeenWelcomeAsync(CancellationToken cancellationToken = default)
    {
        return string.Equals(await GetSettingAsync(WelcomeSeenKey, cancellationToken), "true", StringComparison.Ordinal);
    }

    public Task MarkWelcomeSeenAsync(CancellationToken cancellationToken = default) =>
        SaveSettingAsync(WelcomeSeenKey, "true", cancellationToken);

    public Task<string?> GetLanguageAsync(CancellationToken cancellationToken = default) =>
        GetSettingAsync(LanguageKey, cancellationToken);

    public Task SaveLanguageAsync(string language, CancellationToken cancellationToken = default) =>
        SaveSettingAsync(LanguageKey, language, cancellationToken);

    public async Task<ApplicationThemeMode?> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        var value = await GetSettingAsync(ThemeKey, cancellationToken);
        return Enum.TryParse<ApplicationThemeMode>(value, ignoreCase: true, out var theme)
            ? theme
            : null;
    }

    public Task SaveThemeAsync(ApplicationThemeMode theme, CancellationToken cancellationToken = default) =>
        SaveSettingAsync(ThemeKey, theme.ToString(), cancellationToken);

    public async Task SaveSelectedNodeAsync(NodeProfile node, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO selected_node (id, beacon_address, name, description)
            VALUES (1, $address, $name, $description)
            ON CONFLICT(id) DO UPDATE SET
                beacon_address = excluded.beacon_address,
                name = excluded.name,
                description = excluded.description;
            """;
        command.Parameters.AddWithValue("$address", node.BeaconAddress);
        command.Parameters.AddWithValue("$name", node.Name);
        command.Parameters.AddWithValue("$description", node.Description);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<NodeProfile?> GetSelectedNodeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT beacon_address, name, description FROM selected_node WHERE id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new NodeProfile(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    public async Task SaveNodeServiceConfigurationAsync(NodeConnection connection, CancellationToken cancellationToken = default)
    {
        await SaveSelectedNodeAsync(connection.Profile, cancellationToken);

        await using var database = await OpenConnectionAsync(cancellationToken);
        await using var command = database.CreateCommand();
        command.CommandText = """
            INSERT INTO selected_node_services (id, configuration_json)
            VALUES (1, $configuration)
            ON CONFLICT(id) DO UPDATE SET configuration_json = excluded.configuration_json;
            """;
        command.Parameters.AddWithValue("$configuration", JsonSerializer.Serialize(NodeServiceConfiguration.From(connection)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<NodeConnection?> GetNodeServiceConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await using var database = await OpenConnectionAsync(cancellationToken);
        await using var command = database.CreateCommand();
        command.CommandText = "SELECT configuration_json FROM selected_node_services WHERE id = 1;";
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<NodeServiceConfiguration>(json)?.ToConnection();
    }

    public async Task SaveProtectedSessionAsync(byte[] protectedData, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO secure_session (id, protected_data) VALUES (1, $data) ON CONFLICT(id) DO UPDATE SET protected_data = excluded.protected_data;";
        command.Parameters.AddWithValue("$data", protectedData);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<byte[]?> GetProtectedSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_data FROM secure_session WHERE id = 1;";
        return await command.ExecuteScalarAsync(cancellationToken) as byte[];
    }

    public async Task DeleteProtectedSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM secure_session WHERE id = 1;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
