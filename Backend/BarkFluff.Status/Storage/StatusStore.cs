using System.Globalization;
using BarkFluff.Status.Probing;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Status.Storage;

/// <summary>Одна проверка слота: ключ (endpoint, service, slot_utc) уникален, повтор слота игнорируется.</summary>
public sealed record ProbeRecord(
    string Endpoint,
    string Service,
    long SlotUtc,
    ProbeState State,
    long CheckedAtUtc,
    long LatencyMs);

/// <summary>Счётчики слотов одного UTC-дня по состояниям.</summary>
public sealed record DayCounts
{
    public static readonly DayCounts Empty = new();

    public long Up { get; init; }
    public long Degraded { get; init; }
    public long Down { get; init; }
    public long Unknown { get; init; }
    public long Starting { get; init; }
    public long Stale { get; init; }

    public long Observed => Up + Degraded + Down + Unknown + Starting + Stale;
}

public sealed record LatestRecord(long SlotUtc, ProbeState State, long CheckedAtUtc);

/// <summary>
/// SQLite-хранилище истории. Пишет и читает один поток (цикл монитора).
/// Батч — одна транзакция: строка слота, дневной счётчик и последний результат меняются вместе,
/// и только если слот действительно добавлен (повтор не удваивает счётчики).
/// </summary>
public sealed class StatusStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public StatusStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
    }

    public void Initialize()
    {
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("PRAGMA busy_timeout=5000;");
        Execute("""
            CREATE TABLE IF NOT EXISTS probe_results(
                endpoint TEXT NOT NULL,
                service TEXT NOT NULL,
                slot_utc INTEGER NOT NULL,
                state TEXT NOT NULL,
                checked_at_utc INTEGER NOT NULL,
                latency_ms INTEGER NOT NULL,
                PRIMARY KEY(endpoint, service, slot_utc)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS daily_counts(
                endpoint TEXT NOT NULL,
                service TEXT NOT NULL,
                day TEXT NOT NULL,
                up INTEGER NOT NULL DEFAULT 0,
                degraded INTEGER NOT NULL DEFAULT 0,
                down INTEGER NOT NULL DEFAULT 0,
                unknown INTEGER NOT NULL DEFAULT 0,
                starting INTEGER NOT NULL DEFAULT 0,
                stale INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(endpoint, service, day)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS latest_results(
                endpoint TEXT NOT NULL,
                service TEXT NOT NULL,
                slot_utc INTEGER NOT NULL,
                state TEXT NOT NULL,
                checked_at_utc INTEGER NOT NULL,
                PRIMARY KEY(endpoint, service)
            ) WITHOUT ROWID;
            """);
    }

    /// <summary>Записывает батч одной транзакцией; возвращает число реально добавленных слотов.</summary>
    public int WriteBatch(IEnumerable<ProbeRecord> records)
    {
        var inserted = 0;
        using var transaction = _connection.BeginTransaction();

        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO probe_results(endpoint, service, slot_utc, state, checked_at_utc, latency_ms)
            VALUES ($endpoint, $service, $slot, $state, $checkedAt, $latency);
            """;

        using var daily = _connection.CreateCommand();
        daily.Transaction = transaction;

        using var latest = _connection.CreateCommand();
        latest.Transaction = transaction;
        latest.CommandText = """
            INSERT INTO latest_results(endpoint, service, slot_utc, state, checked_at_utc)
            VALUES ($endpoint, $service, $slot, $state, $checkedAt)
            ON CONFLICT(endpoint, service) DO UPDATE SET
                slot_utc = excluded.slot_utc,
                state = excluded.state,
                checked_at_utc = excluded.checked_at_utc
            WHERE excluded.slot_utc > latest_results.slot_utc;
            """;

        foreach (var record in records)
        {
            var state = StateName(record.State);
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$endpoint", record.Endpoint);
            insert.Parameters.AddWithValue("$service", record.Service);
            insert.Parameters.AddWithValue("$slot", record.SlotUtc);
            insert.Parameters.AddWithValue("$state", state);
            insert.Parameters.AddWithValue("$checkedAt", record.CheckedAtUtc);
            insert.Parameters.AddWithValue("$latency", record.LatencyMs);
            if (insert.ExecuteNonQuery() != 1)
                continue;

            inserted++;
            // Имя колонки — константа из ColumnFor; значения всегда параметры.
            var column = ColumnFor(record.State);
            daily.CommandText = $"""
                INSERT INTO daily_counts(endpoint, service, day, {column}) VALUES ($endpoint, $service, $day, 1)
                ON CONFLICT(endpoint, service, day) DO UPDATE SET {column} = {column} + 1;
                """;
            daily.Parameters.Clear();
            daily.Parameters.AddWithValue("$endpoint", record.Endpoint);
            daily.Parameters.AddWithValue("$service", record.Service);
            daily.Parameters.AddWithValue("$day", DayOf(record.SlotUtc));
            daily.ExecuteNonQuery();

            latest.Parameters.Clear();
            latest.Parameters.AddWithValue("$endpoint", record.Endpoint);
            latest.Parameters.AddWithValue("$service", record.Service);
            latest.Parameters.AddWithValue("$slot", record.SlotUtc);
            latest.Parameters.AddWithValue("$state", state);
            latest.Parameters.AddWithValue("$checkedAt", record.CheckedAtUtc);
            latest.ExecuteNonQuery();
        }

        transaction.Commit();
        return inserted;
    }

    public long? FirstSlot(string endpoint, string service)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT MIN(slot_utc) FROM probe_results WHERE endpoint = $endpoint AND service = $service;";
        command.Parameters.AddWithValue("$endpoint", endpoint);
        command.Parameters.AddWithValue("$service", service);
        return command.ExecuteScalar() is long slot ? slot : null;
    }

    public LatestRecord? Latest(string endpoint, string service)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT slot_utc, state, checked_at_utc FROM latest_results
            WHERE endpoint = $endpoint AND service = $service;
            """;
        command.Parameters.AddWithValue("$endpoint", endpoint);
        command.Parameters.AddWithValue("$service", service);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new LatestRecord(reader.GetInt64(0), ParseState(reader.GetString(1)), reader.GetInt64(2));
    }

    /// <summary>Дневные счётчики с fromDay включительно (yyyy-MM-dd, UTC).</summary>
    public IReadOnlyDictionary<string, DayCounts> Days(string endpoint, string service, string fromDay)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT day, up, degraded, down, unknown, starting, stale FROM daily_counts
            WHERE endpoint = $endpoint AND service = $service AND day >= $fromDay;
            """;
        command.Parameters.AddWithValue("$endpoint", endpoint);
        command.Parameters.AddWithValue("$service", service);
        command.Parameters.AddWithValue("$fromDay", fromDay);

        var result = new Dictionary<string, DayCounts>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = new DayCounts
            {
                Up = reader.GetInt64(1),
                Degraded = reader.GetInt64(2),
                Down = reader.GetInt64(3),
                Unknown = reader.GetInt64(4),
                Starting = reader.GetInt64(5),
                Stale = reader.GetInt64(6),
            };
        }
        return result;
    }

    /// <summary>Удаляет слоты раньше cutoffSlot и дни раньше cutoffDay (yyyy-MM-dd, UTC).</summary>
    public void Prune(long cutoffSlot, string cutoffDay)
    {
        using var transaction = _connection.BeginTransaction();
        using (var slots = _connection.CreateCommand())
        {
            slots.Transaction = transaction;
            slots.CommandText = "DELETE FROM probe_results WHERE slot_utc < $cutoff;";
            slots.Parameters.AddWithValue("$cutoff", cutoffSlot);
            slots.ExecuteNonQuery();
        }
        using (var days = _connection.CreateCommand())
        {
            days.Transaction = transaction;
            days.CommandText = "DELETE FROM daily_counts WHERE day < $cutoff;";
            days.Parameters.AddWithValue("$cutoff", cutoffDay);
            days.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static string DayOf(long slotUtc) =>
        DateTimeOffset.FromUnixTimeSeconds(slotUtc).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string StateName(ProbeState state) => state switch
    {
        ProbeState.Up => "up",
        ProbeState.Degraded => "degraded",
        ProbeState.Down => "down",
        ProbeState.Starting => "starting",
        ProbeState.Unknown => "unknown",
        ProbeState.Stale => "stale",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static ProbeState ParseState(string name) => name switch
    {
        "up" => ProbeState.Up,
        "degraded" => ProbeState.Degraded,
        "down" => ProbeState.Down,
        "starting" => ProbeState.Starting,
        "unknown" => ProbeState.Unknown,
        "stale" => ProbeState.Stale,
        _ => throw new InvalidDataException($"Неизвестное состояние в хранилище: {name}"),
    };

    private static string ColumnFor(ProbeState state) => StateName(state);

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
