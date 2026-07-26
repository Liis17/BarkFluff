namespace BarkFluff.Shared.Exceptions.Messages;

// Upsert remote-профиля от Federation отвергнут (LocalUuidCollision / ServerNameMismatch).
// Permanent отказ (docs/rearch/05, шаг 2 ImportFederatedChat).
public class RemoteProfileRejectedException : BaseGrpcException
{
    public override string ErrorCode => "B8FAD4E7-6AB1-4FBA-F17E-80D3CA0EB08";
    public override string ErrorMessage => "Профиль инициатора отклонён";
}
