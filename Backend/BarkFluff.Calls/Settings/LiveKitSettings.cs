namespace BarkFluff.Calls.Settings;

/// <summary>
/// Конфигурация LiveKit (секция "LiveKit" в Settings-сервисе).
/// Креды должны совпадать с keys в конфиге самого LiveKit-сервера.
/// </summary>
public class LiveKitSettings
{
    /// <summary>Внутренний WS/WSS-адрес LiveKit для серверного RoomServiceClient.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Публичный WSS-адрес LiveKit для входа клиента в комнату (дублируется в Beacon).</summary>
    public string PublicUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Секрет (≥32 байт — требование LiveKit SDK для подписи токенов).</summary>
    public string ApiSecret { get; set; } = string.Empty;
}
