namespace BarkFluff.Shared.Exceptions.Messages;

// Invitee запретил входящие федеративные DM (Privacy.DenyFederatedDm=true, этап 2.5,
// docs/rearch/05-chat-replication.md, «Создание чата»). Permanent отказ ImportFederatedChat.
// ErrorCode — литеральная строка (не GUID, как у остальных Fed-исключений): OutboxDispatcher
// сравнивает x-error-code строкой "FederatedDmRejected", чтобы решить публиковать ли
// FederatedChatRejectedEvent на origin-ноде.
public class FederatedDmRejectedException : BaseGrpcException
{
    public override string ErrorCode => "FederatedDmRejected";
    public override string ErrorMessage => "Получатель запретил входящие сообщения с других серверов";
}
