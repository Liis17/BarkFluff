namespace BarkFluff.Shared.Exceptions.FastAuth;

public class FastAuthSessionNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "A5E94C7D-1B82-4F36-9CDE-78B1F4A7E2C5";

    public override string ErrorMessage => "Сессия быстрой авторизации не найдена";
}
