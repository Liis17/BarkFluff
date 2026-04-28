namespace BarkFluff.Shared.Exceptions.FastAuth;

public class FastAuthInvalidConfirmationCodeException : BaseGrpcException
{
    public override string ErrorCode => "7B3F8E92-5D14-4C68-A7E2-1F9B6D3C8A45";

    public override string ErrorMessage => "Неверный код подтверждения быстрой авторизации";
}
