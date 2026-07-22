namespace BarkFluff.Bots.Messages;

/// <summary>
/// Fan-out сигнал «у бота появился новый update». Публикуется NewMessageConsumer (competing —
/// сохраняет update один раз) ПОСЛЕ записи в БД; консьюмер на КАЖДОМ инстансе будит свои локальные
/// long-poll/стрим-waiter'ы (BotUpdateNotifier). Так сигнал доходит до poller'а, живущего на другом
/// инстансе. Сам update шарится через БД, поэтому сигнал не несёт полезной нагрузки.
/// </summary>
public class BotUpdateSignalEvent
{
    public long BotId { get; set; }
}
