namespace BarkFluff.Federation.Services;

/// <summary>
/// Настройки скачивания federated-файлов (этап 3.2). Читаются один раз при старте.
/// </summary>
public class FederatedFileOptions
{
    public FederatedFileOptions(IConfiguration configuration)
    {
        S2SConnectTimeout = Seconds(configuration, "Federation:S2SConnectTimeout", 10);
        RemoteFileIdleTimeout = Seconds(configuration, "Federation:RemoteFileIdleTimeout", 60);
    }

    /// <summary>
    /// Таймаут установления S2S-соединения. Отдельно от deadline: сам стрим большого файла
    /// законно долгий, а вот «не смогли подключиться» должно выясняться быстро.
    /// </summary>
    public TimeSpan S2SConnectTimeout { get; }

    /// <summary>
    /// Максимальное молчание origin внутри стрима. Перезаряжается на каждом полученном чанке:
    /// медленный, но живой origin допустим — замолчавший нет.
    /// </summary>
    public TimeSpan RemoteFileIdleTimeout { get; }

    private static TimeSpan Seconds(IConfiguration configuration, string key, int fallbackSeconds)
    {
        var value = configuration.GetValue<int?>(key) ?? fallbackSeconds;
        return TimeSpan.FromSeconds(value > 0 ? value : fallbackSeconds);
    }
}
