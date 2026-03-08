namespace BarkFluff.Shared.Exceptions.Identity;

public class InvalidLoginOrPasswordException : BaseGrpcException
{
    public override string ErrorCode => "21BFB9B5-C377-45D1-9B15-6B7F3432B397";

    public override string ErrorMessage => "Неверный логин или пароль";
}