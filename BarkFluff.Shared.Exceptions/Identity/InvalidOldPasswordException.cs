namespace BarkFluff.Shared.Exceptions.Identity;

public class InvalidOldPasswordException : BaseGrpcException
{
    public override string ErrorCode => "A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04";

    public override string ErrorMessage => "Неверный старый пароль";
}
