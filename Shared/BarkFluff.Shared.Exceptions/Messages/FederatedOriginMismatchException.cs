namespace BarkFluff.Shared.Exceptions.Messages;

// "Нода говорит только за своих" (docs/rearch/05, ApplyFederatedEdit/Delete/Read, закрывает P2-02):
// автор правимого/удаляемого сообщения (или читатель) принадлежит не той ноде, что прислала событие.
// Permanent отказ — событие не переигрывается ретраем.
public class FederatedOriginMismatchException : BaseGrpcException
{
    public override string ErrorCode => "F1D8B2C6-4E0A-4B9F-9D1E-6C2A8F0B3E06";
    public override string ErrorMessage => "Нода не является домашней для автора/читателя события";
}
