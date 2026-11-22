using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Append-only audit log of critical admin actions.
/// </summary>
public class AuditService
{
    public const int RetentionDays = 90;
    public const int MaxFilterLength = 200;

    private const string SuccessOutcome = "success";
    private const int ActionScanBatchSize = 500;

    private readonly AuditDbContext _db;
    private readonly ILogger<AuditService> _logger;
    private readonly Timer _cleanupTimer;

    public AuditService(AuditDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromHours(6), TimeSpan.FromHours(24));
    }

    public void Log(AuditLogEntry entry)
    {
        try
        {
            _db.Entries.Insert(entry);
        }
        catch (Exception ex)
        {
            // Аудит не должен ломать выполнение самого действия
            _logger.LogError(ex, "Failed to write audit entry for {Action}", entry.Action);
        }
    }

    /// <summary>
    /// Возвращает страницу записей (новые сверху). Фильтры и keyset-курсор <paramref name="beforeUtc"/>
    /// применяются до <paramref name="limit"/>, поэтому страница заполняется подходящими записями из всей истории.
    /// Подстрока действия проверяется после чтения порций из LiteDB (см. комментарий ниже).
    /// </summary>
    public List<AuditLogEntry> GetEntries(int limit, DateTime? beforeUtc, AuditEntryFilter? filter = null)
    {
        filter ??= new AuditEntryFilter();
        var action = NormalizeText(filter.Action);
        if (action is null)
        {
            return QueryPage(limit, beforeUtc, filter);
        }

        // LiteDB транслирует Contains в LIKE, где % и _ — шаблоны без экранирования, поэтому подстрока
        // действия проверяется здесь. Остальные фильтры и keyset выполняются в LiteDB порциями, пока страница
        // не заполнится: фильтрация по-прежнему идёт до limit и охватывает всю историю.
        var result = new List<AuditLogEntry>(limit);
        var cursor = beforeUtc;
        while (result.Count < limit)
        {
            var batch = QueryPage(ActionScanBatchSize, cursor, filter);
            result.AddRange(batch.Where(e => e.Action.ToLowerInvariant().Contains(action, StringComparison.Ordinal)));
            if (batch.Count < ActionScanBatchSize)
            {
                break;
            }

            cursor = batch[^1].At;
        }

        return result.Take(limit).ToList();
    }

    private List<AuditLogEntry> QueryPage(int limit, DateTime? beforeUtc, AuditEntryFilter filter)
    {
        var query = _db.Entries.Query();

        if (beforeUtc.HasValue)
        {
            var cutoff = ToUtcInstant(beforeUtc.Value);
            query = query.Where(x => x.At < cutoff);
        }

        var admin = NormalizeText(filter.AdminUsername, stripLeadingAt: true);
        if (admin is not null)
        {
            query = query.Where(x => x.AdminUsername != null && x.AdminUsername.ToLower() == admin);
        }

        var outcome = NormalizeText(filter.Outcome);
        if (outcome == SuccessOutcome)
        {
            query = query.Where(x => x.Outcome == "ok" || x.Outcome == "confirmed");
        }
        else if (outcome is not null)
        {
            query = query.Where(x => x.Outcome == outcome);
        }

        if (filter.FromUtc is { } from)
        {
            var lowerBound = ToUtcInstant(from);
            query = query.Where(x => x.At >= lowerBound);
        }

        if (filter.ToUtc is { } to)
        {
            var upperBound = ToUtcInstant(to);
            query = query.Where(x => x.At < upperBound);
        }

        return query.OrderByDescending(x => x.At).Limit(limit).ToList();
    }

    /// <summary>
    /// Проверяет фильтры запроса. Возвращает текст ошибки либо null, если фильтры допустимы.
    /// </summary>
    public static string? ValidateFilter(AuditEntryFilter filter)
    {
        if (IsTooLong(filter.AdminUsername) || IsTooLong(filter.Action) || IsTooLong(filter.Outcome))
        {
            return $"Значение фильтра не длиннее {MaxFilterLength} символов";
        }

        if (filter.FromUtc is { } from && filter.ToUtc is { } to && ToUtcInstant(from) > ToUtcInstant(to))
        {
            return "fromUtc не может быть позже toUtc";
        }

        return null;
    }

    /// <summary>
    /// Уникальные имена администраторов и ключи действий из сохранённой истории (для подсказок UI).
    /// </summary>
    public (List<string> Admins, List<string> Actions) GetFacets()
    {
        var admins = _db.Entries.Query()
            .Select(x => x.AdminUsername)
            .ToList()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actions = _db.Entries.Query()
            .Select(x => x.Action)
            .ToList()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return (admins, actions);
    }

    private static bool IsTooLong(string? value) => value is not null && value.Length > MaxFilterLength;

    /// <summary>
    /// Приводит текст фильтра к нижнему регистру без пробелов; пустой ввод означает «фильтр не задан».
    /// </summary>
    private static string? NormalizeText(string? value, bool stripLeadingAt = false)
    {
        var text = value?.Trim();
        if (stripLeadingAt && text is not null && text.StartsWith('@'))
        {
            text = text[1..].TrimStart();
        }

        return string.IsNullOrEmpty(text) ? null : text.ToLowerInvariant();
    }

    /// <summary>
    /// Время без зоны трактуется как UTC; локальное время переводится в UTC. Сравнение в LiteDB идёт по UTC.
    /// </summary>
    private static DateTime ToUtcInstant(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private void Cleanup()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            _db.Entries.DeleteMany(x => x.At < cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log cleanup failed");
        }
    }
}
