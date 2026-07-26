namespace BarkFluff.Shared.Exceptions.Messages;

// Федеративный чат существует локально, но больше не Active (Rejected — privacy-отказ 2.5,
// Merged — 2.7). В отличие от ChatUnknownException (чат ещё не синхронизирован, RETRY имеет
// смысл) — это перманентное состояние: ретраить бессмысленно, событие уедет в DeadLetter.
public class FederatedChatNotActiveException : BaseGrpcException
{
    public override string ErrorCode => "C42639DE-4619-48BE-ADE4-EA9B5DEA7E43";
    public override string ErrorMessage => "Федеративный чат более не активен на этой стороне";
}
