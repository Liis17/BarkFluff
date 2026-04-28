namespace BarkFluff.Shared.Exceptions.FastAuth;

public class FastAuthInvalidStateException : BaseGrpcException
{
    public override string ErrorCode => "3C8A1E5F-7D29-4B83-9E16-5A2C8F4B7D31";

    public override string ErrorMessage => "Сессия быстрой авторизации находится в недопустимом состоянии для этой операции";
}
