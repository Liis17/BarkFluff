namespace BarkFluff.Messages.Domain;

// Статус федеративного DM (этап 2.3). Rejected — privacy-отказ invitee (2.5).
// Merged — чат проиграл гонку одновременного создания (2.7), события перенаправляются на победителя.
public enum FederatedStatus
{
    Active = 0,
    Rejected = 1,
    Merged = 2,
}
