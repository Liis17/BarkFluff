namespace BarkFluff.Shared.Exceptions.Messages;

/// <summary>
/// Снапшот пересылки, пришедший с чужой ноды, не прошёл валидацию. Permanent (REJECTED), как и
/// <see cref="FederatedAttachmentInvalidException"/>: повторная доставка того же битого события
/// ничего не исправит и только зациклит outbox отправителя.
/// </summary>
public class FederatedForwardInvalidException : BaseGrpcException
{
    public override string ErrorCode => "5D3E8B1C-46A7-42F9-8C05-B7E29D4A6013";

    public override string ErrorMessage => "Некорректный снапшот пересланного сообщения в федеративном событии";
}
