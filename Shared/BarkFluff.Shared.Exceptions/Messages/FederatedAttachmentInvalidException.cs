namespace BarkFluff.Shared.Exceptions.Messages;

// Снапшот вложения fed-сообщения не прошёл валидацию (этап 3.1, docs/rearch/06-files.md):
// битый file_id/preview_file_id, отрицательный или превышающий лимит размер, неизвестный тип,
// слишком длинное имя. Permanent (дефолтный FailedPrecondition → REJECTED): повторная доставка
// того же битого события ничего не исправит и только зациклит outbox отправителя.
public class FederatedAttachmentInvalidException : BaseGrpcException
{
    public override string ErrorCode => "C2F7A93E-5B41-4D8A-9E63-1F0D7B2C4A55";
    public override string ErrorMessage => "Некорректный снапшот вложения федеративного сообщения";
}
