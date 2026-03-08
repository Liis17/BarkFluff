namespace BarkFluff.Shared.Exceptions.Identity;

public class ConfirmationCodeExpiredException : BaseGrpcException
{
    public override string ErrorCode => "7AABF347-1210-4B14-A93B-2BA8574D74E7";

    public override string ErrorMessage => "Код подтверждения больше недействителен";
}