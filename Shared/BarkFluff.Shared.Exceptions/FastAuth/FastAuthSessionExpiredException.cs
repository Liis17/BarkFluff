namespace BarkFluff.Shared.Exceptions.FastAuth;

public class FastAuthSessionExpiredException : BaseGrpcException
{
    public override string ErrorCode => "D2F71E8A-3C5B-4197-8A6D-4E9B27C5F1A8";

    public override string ErrorMessage => "Сессия быстрой авторизации истекла";
}
