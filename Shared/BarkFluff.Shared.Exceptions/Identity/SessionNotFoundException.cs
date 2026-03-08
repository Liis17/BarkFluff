namespace BarkFluff.Shared.Exceptions.Identity;

public class SessionNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "011BF29A-2DE6-4A63-BF8D-3F36AE730D9D";

    public override string ErrorMessage => "Сессия не найдена";
}