using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Messages;

// Сообщение по FederatedId неизвестно локально (docs/rearch/05, ApplyFederatedEdit/Delete).
// RETRY: catch-up дотянет сообщение, затем правка применится (этап 2.6).
public class FederatedMessageUnknownException : BaseGrpcException
{
    public override string ErrorCode => "E5C7A0B4-3D9F-4F8E-CE4B-5DA0F7B4E05";
    public override string ErrorMessage => "Сообщение ещё не синхронизировано";
    public override StatusCode StatusCode => StatusCode.NotFound;
}
