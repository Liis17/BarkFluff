namespace BarkFluff.Shared.Exceptions.Messages;

// Remote-получатель неизвестен в RemoteUsers (docs/rearch/05): клиент обязан резолвнуть FID
// через Users.ResolveFederatedUser перед отправкой. Если uuid не в RemoteUsers — значит резолва
// не было, отправка отклоняется.
public class RemoteUserNotResolvedException : BaseGrpcException
{
    public override string ErrorCode => "A7E9C3D6-5FA0-4FAF-E06D-7FC2B9D6A07";
    public override string ErrorMessage => "Сначала резолвните удалённого пользователя";
}
