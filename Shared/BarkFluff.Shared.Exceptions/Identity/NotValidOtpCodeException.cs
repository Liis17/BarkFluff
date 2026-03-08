namespace BarkFluff.Shared.Exceptions.Identity;

public class NotValidOtpCodeException : BaseGrpcException
{
    public override string ErrorCode => "803B632C-4457-4B05-9435-9C3DD0F41E00";

    public override string ErrorMessage => "Неверный код 2FA";
}