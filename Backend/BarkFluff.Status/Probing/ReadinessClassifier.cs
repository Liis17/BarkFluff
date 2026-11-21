using System.Globalization;
using System.Text.Json;

namespace BarkFluff.Status.Probing;

/// <summary>
/// Классификация ответа /health/ready. Контракт ReadinessSnapshot: 200 для healthy/degraded/starting/unknown,
/// 503 для down. Читаются только status и checkedAtUtc; остальные поля игнорируются.
/// Чистая функция: время передаётся явно.
/// </summary>
public static class ReadinessClassifier
{
    /// <summary>Сколько starting считается прогревом, отсчитанным от checkedAtUtc снимка.</summary>
    public static readonly TimeSpan StartingGrace = TimeSpan.FromMinutes(2);

    /// <summary>Снимок старше этого возраста — stale: монитор ноды не обновлялся (интервал 15 с).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(90);

    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromSeconds(30);

    /// <param name="statusCode">HTTP-код ответа; перенаправления не выполняются и дают Down.</param>
    /// <param name="body">Тело ответа; null — не прочитано (превышен лимит) или ошибка чтения.</param>
    public static ProbeState Classify(int statusCode, string? body, DateTimeOffset now)
    {
        if (body is null)
            return ProbeState.Down;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ProbeState.Down;
            if (!TryGetString(root, "status", out var status) || !TryGetTime(root, "checkedAtUtc", out var checkedAt))
                return ProbeState.Down;

            var age = now - checkedAt;
            if (status == "starting")
            {
                // Прогрев: только 200 и только пока снимок свежий в пределах grace; дальше — prolonged starting = down.
                if (statusCode != 200)
                    return ProbeState.Down;
                return age <= StartingGrace && age >= -MaxFutureSkew ? ProbeState.Starting : ProbeState.Down;
            }

            // Контракт (статус + HTTP-код) проверяется раньше возраста: 503 down и любая неизвестная пара — Down
            // независимо от checkedAtUtc. Stale относится только к корректным 200-ответам.
            return (status, statusCode) switch
            {
                ("down", 503) => ProbeState.Down,
                ("healthy", 200) => ByAge(age, ProbeState.Up),
                ("degraded", 200) => ByAge(age, ProbeState.Degraded),
                ("unknown", 200) => ByAge(age, ProbeState.Unknown),
                _ => ProbeState.Down,
            };
        }
        catch (JsonException)
        {
            return ProbeState.Down;
        }
    }

    private static ProbeState ByAge(TimeSpan age, ProbeState fresh) =>
        age > MaxAge || age < -MaxFutureSkew ? ProbeState.Stale : fresh;

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGetTime(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }
}
