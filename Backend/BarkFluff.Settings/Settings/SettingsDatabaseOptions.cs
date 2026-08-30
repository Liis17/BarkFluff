using Npgsql;

namespace BarkFluff.Settings.Settings;

public sealed record SettingsDatabaseOptions(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    string AdminDatabase)
{
    public string ConnectionString => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password
    }.ConnectionString;

    public static SettingsDatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var host = Get(configuration, "SETTINGS_HOST", "CONFIGURATION_HOST");
        var username = Get(configuration, "SETTINGS_USERNAME", "CONFIGURATION_USERNAME");
        var password = Get(configuration, "SETTINGS_PASSWORD", "CONFIGURATION_PASSWORD");

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Settings database is not configured. Set SETTINGS_HOST, SETTINGS_USERNAME and SETTINGS_PASSWORD.");
        }

        return new SettingsDatabaseOptions(
            host,
            GetInt(configuration, 5432, "SETTINGS_DBPORT", "CONFIGURATION_DBPORT"),
            Get(configuration, "SETTINGS_DATABASE", "CONFIGURATION_DATABASE") ?? "settings",
            username,
            password,
            Get(configuration, "SETTINGS_ADMIN_DATABASE") ?? "postgres");
    }

    private static string? Get(IConfiguration configuration, params string[] keys)
    {
        return keys.Select(key => configuration[key]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int GetInt(IConfiguration configuration, int fallback, params string[] keys)
    {
        var value = Get(configuration, keys);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
