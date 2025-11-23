using BarkFluff.DBEditor.Models;

using Npgsql;

namespace BarkFluff.DBEditor.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(DbCredentials creds)
        {
            string host = creds.Host;
            string port = "5432";

            if (host.Contains(":"))
            {
                var parts = host.Split(':');
                host = parts[0];
                port = parts[1];
            }

            _connectionString = $"Host={host};Port={port};Database={creds.Database};Username={creds.Username};Password={creds.Password}";
        }

        public async Task<List<ConfigItem>> GetAllConfigsAsync()
        {
            var list = new List<ConfigItem>();
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            string sql = "SELECT \"Id\", \"Section\", \"Key\", \"Value\", \"EditedAt\", \"EditedBy\", \"EditedFrom\", \"ServiceId\" FROM \"Configurations\" ORDER BY \"Id\"";

            using var cmd = new NpgsqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string val = reader.IsDBNull(3) ? null : reader.GetString(3);

                list.Add(new ConfigItem
                {
                    Id = reader.GetInt32(0),
                    Section = reader.GetString(1),
                    Key = reader.GetString(2),
                    Value = val,
                    OriginalValue = val,
                    EditedAt = reader.GetDateTime(4),
                    EditedBy = reader.GetString(5),
                    EditedFrom = reader.GetString(6),
                    ServiceId = reader.GetInt32(7)
                });
            }
            return list;
        }

        public async Task UpdateConfigAsync(int id, string newValue)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            string sql = @"UPDATE ""Configurations"" 
                           SET ""Value"" = @val, ""EditedAt"" = @date, ""EditedBy"" = 'AppUser', ""EditedFrom"" = 'WPF-Client' 
                           WHERE ""Id"" = @id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("val", (object)newValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("date", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task TestConnectionAsync()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
        }
    }
}
