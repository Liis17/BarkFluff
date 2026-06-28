namespace BarkFluff.Shared.Queue.Messages;

/// <summary>
/// Команда CloudMessaging погасить push-нотификацию входящего звонка на всех
/// FCM-устройствах получателей (звонок принят/отклонён/завершён/таймаут/занято).
/// Публикуется из BarkFluff.Calls при завершении ринга.
/// </summary>
public class CallDismissPushEvent
{
    public Guid CallId { get; set; }

    public List<long> RecipientUserIds { get; set; } = [];

    /// <summary>accepted / rejected / ended / timeout / busy.</summary>
    public string Reason { get; set; } = string.Empty;
}
