namespace BarkFluff.Shared.Exceptions.Identity;

public class ConfirmationCodeNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "56D9BB63-DA40-40DE-9C56-7487A1A437D0";

    public override string ErrorMessage => "Код потверждения не найден";
}