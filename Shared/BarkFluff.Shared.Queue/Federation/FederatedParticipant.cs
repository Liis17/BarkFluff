namespace BarkFluff.Shared.Queue.Federation;

// Участник федеративного чата с чужой ноды. Заполняется сервисом Messages (этап 2.3) и используется
// Federation для построения исходящих событий в outbox (по одной строке на ServerName).
public class FederatedParticipant
{
    public Guid Uuid { get; set; }

    public string ServerName { get; set; } = string.Empty;
}
