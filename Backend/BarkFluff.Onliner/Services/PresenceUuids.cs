namespace BarkFluff.Onliner.Services;

/// <summary>
/// Разбор и лимиты uuid-ветки подписок (этап 4.2).
/// </summary>
public static class PresenceUuids
{
    /// <summary>
    /// Максимум remote-uuid в одной подписке. Подписка — вектор ресурсного злоупотребления,
    /// а на <c>user_ids</c> исторического лимита нет, поэтому ограничиваем только новую ветку.
    /// </summary>
    public const int MaxSubscriptionUuids = 500;

    /// <summary>
    /// Разобрать список uuid из proto. Невалидный элемент молча отбрасывается: он не должен
    /// валить весь вызов — остальные подписки клиента при этом продолжают работать.
    /// </summary>
    public static List<Guid> Parse(IEnumerable<string> raw)
    {
        var parsed = new List<Guid>();

        foreach (var value in raw)
        {
            if (Guid.TryParse(value, out var uuid) && !parsed.Contains(uuid))
            {
                parsed.Add(uuid);
            }
        }

        return parsed;
    }
}
