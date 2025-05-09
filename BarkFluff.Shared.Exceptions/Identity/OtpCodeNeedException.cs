namespace BarkFluff.Shared.Exceptions.Identity;

public class OtpCodeNeedException : BaseGrpcException
{
    public override string ErrorCode => "C1576884-12D8-4722-A7EE-9F9789AD1265";

    public override string ErrorMessage => "Необходимо 2FA ввести код";
}