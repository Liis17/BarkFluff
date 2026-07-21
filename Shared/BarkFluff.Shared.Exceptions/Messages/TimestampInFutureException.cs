namespace BarkFluff.Shared.Exceptions.Messages;

// Метка входящего fed-события в будущем (docs/rearch/05-chat-replication.md, «Валидация импорта»):
// origin_ts_ms > now + окно подписи — permanent отказ, иначе «вечно побеждает» в LWW.
public class TimestampInFutureException : BaseGrpcException
{
    public override string ErrorCode => "E1A8B514-3C4E-4F2B-9A1D-7C5E2B8F1A01";
    public override string ErrorMessage => "Метка события из будущего";
}
