namespace BarkFluff.Shared.Exceptions.Identity;

public class NotSetUsernameOrEmailException : BaseGrpcException
{
    public override string ErrorCode => "55872FA3-4F77-4C5A-B471-C25699BA20C0";

    public override string ErrorMessage => "Не передан ни логин ни email";
}