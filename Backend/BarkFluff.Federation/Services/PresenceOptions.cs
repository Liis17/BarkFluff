namespace BarkFluff.Federation.Services;

/// <summary>
/// Настройки presence/typing-моста (этапы 4.3/4.4). Читаются один раз при старте:
/// значения меняются перезапуском ноды, а не на лету.
/// </summary>
public class PresenceOptions
{
    public PresenceOptions(IConfiguration configuration)
    {
        MaxSubscriptionSize = Read(configuration, "Federation:MaxPresenceSubscriptionSize", 500);
        InterestTtl = Seconds(configuration, "Federation:PresenceInterestTtlSeconds", 60);
        ReconcileInterval = Seconds(configuration, "Federation:PresenceReconcileSeconds", 10);
        ResubscribeMinInterval = Seconds(configuration, "Federation:PresenceResubscribeMinSeconds", 5);
        CoalesceWindow = Seconds(configuration, "Federation:PresenceCoalesceSeconds", 5);
        ResyncInterval = Seconds(configuration, "Federation:PresenceResyncSeconds", 300);

        TypingCoalesceWindow = Seconds(configuration, "Federation:TypingCoalesceSeconds", 2);
        TypingDeadline = TimeSpan.FromMilliseconds(
            Read(configuration, "Federation:TypingDeadlineMs", 2000));
        TypingRateLimitPerOriginPerMinute = Read(
            configuration, "Federation:TypingRateLimitPerOriginPerMinute", 600);
        TypingValidationCacheTtl = Seconds(configuration, "Federation:TypingValidationCacheSeconds", 30);
    }

    /// <summary>Максимум uuid в одной S2S-подписке (обе стороны: лимит и защита от разрастания).</summary>
    public int MaxSubscriptionSize { get; }

    /// <summary>Время жизни записи интереса инстанса Onliner (≈ 3 × интервала его репортера).</summary>
    public TimeSpan InterestTtl { get; }

    /// <summary>Как часто сверяем «желаемые» подписки с фактическими.</summary>
    public TimeSpan ReconcileInterval { get; }

    /// <summary>Дебаунс переоткрытия стрима — чтобы частые изменения набора не устраивали флаппинг.</summary>
    public TimeSpan ResubscribeMinInterval { get; }

    /// <summary>Не чаще одного события на пару (пользователь, стрим) за это окно.</summary>
    public TimeSpan CoalesceWindow { get; }

    /// <summary>Периодический ресинк снимка — страховка от пропущенного fan-out-события.</summary>
    public TimeSpan ResyncInterval { get; }

    /// <summary>Не чаще одной отправки typing в окно на ключ (чат, отправитель, нода).</summary>
    public TimeSpan TypingCoalesceWindow { get; }

    /// <summary>Короткий deadline S2S-вызова typing: он эфемерен, ждать долго бессмысленно.</summary>
    public TimeSpan TypingDeadline { get; }

    /// <summary>Лимит входящих typing per-origin в минуту.</summary>
    public int TypingRateLimitPerOriginPerMinute { get; }

    /// <summary>TTL кеша валидации (автор принадлежит origin + состоит в чате).</summary>
    public TimeSpan TypingValidationCacheTtl { get; }

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue<int?>(key) ?? fallback;
        return value > 0 ? value : fallback;
    }

    private static TimeSpan Seconds(IConfiguration configuration, string key, int fallbackSeconds)
        => TimeSpan.FromSeconds(Read(configuration, key, fallbackSeconds));
}
