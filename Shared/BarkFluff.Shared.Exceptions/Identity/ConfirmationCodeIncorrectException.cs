namespace BarkFluff.Shared.Exceptions.Identity;

public class ConfirmationCodeIncorrectException : BaseGrpcException
{
    public override string ErrorCode => "4396D597-D605-4040-AF0F-D9168F0CA034";

    public override string ErrorMessage => "Неверный код подтверждения";
}