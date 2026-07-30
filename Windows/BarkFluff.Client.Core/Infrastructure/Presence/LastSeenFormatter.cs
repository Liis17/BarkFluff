using BarkFluff.Client.Core.Infrastructure.Localization;

namespace BarkFluff.Client.Core.Infrastructure.Presence;

/// <summary>
/// Подпись присутствия собеседника. Чистая функция с явным <c>now</c>: иначе тест на «сегодня»
/// зависел бы от момента запуска.
/// </summary>
public static class LastSeenFormatter
{
    public static string Format(ILocalizationService localization, bool isOnline, DateTimeOffset? lastSeen, DateTimeOffset now)
    {
        if (isOnline)
        {
            return localization.GetString("Messenger_StatusOnline");
        }

        // MinValue приходит от сервера, когда пользователь не появлялся в сети ни разу.
        if (lastSeen is not { } seen || seen == DateTimeOffset.MinValue)
        {
            return localization.GetString("Messenger_StatusOffline");
        }

        var seenLocal = seen.ToLocalTime();
        return seenLocal.Date == now.ToLocalTime().Date
            ? string.Format(localization.GetString("Messenger_StatusLastSeenAt"), seenLocal.ToString("HH:mm"))
            : string.Format(localization.GetString("Messenger_StatusLastSeenOn"), seenLocal.ToString("d"));
    }
}
