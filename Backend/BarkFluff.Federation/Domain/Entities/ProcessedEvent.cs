namespace BarkFluff.Federation.Domain.Entities;

// Идемпотентность входящих событий (этап 2.2). TTL &gt; окна ретраев отправителя — janitor чистит
// старше 14 дней. До очистки повторная доставка отвечает AlreadyProcessed.
public class ProcessedEvent
{
    public Guid EventId { get; set; }

    public string OriginServer { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; }
}
