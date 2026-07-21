namespace BarkFluff.Shared.Queue.Federation;

// Origin-нода получила перманентный privacy-отказ ChatCreated (invitee.DenyFederatedDm=true) от
// партнёра (этап 2.5, docs/rearch/05-chat-replication.md, «Создание чата»). Publisher — Federation
// (OutboxDispatcher, DeadLetter с ErrorCode="FederatedDmRejected"); consumer — Messages, помечает
// Chat.FederatedStatus = Rejected.
public class FederatedChatRejectedEvent
{
    public Guid ChatId { get; set; }

    public string Reason { get; set; } = string.Empty;
}
