using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Messages;

// Входящее fed-событие для чата, которого нет локально (docs/rearch/05, «Catch-up после даунтайма»).
// RETRY: Federation повторит доставку; параллельно запускается catch-up истории (этап 2.6).
public class ChatUnknownException : BaseGrpcException
{
    public override string ErrorCode => "D4B6F9A3-2C8E-4F7D-BD3A-4C9E2F6A3D04";
    public override string ErrorMessage => "Чат ещё не синхронизирован";
    public override StatusCode StatusCode => StatusCode.NotFound;
}
