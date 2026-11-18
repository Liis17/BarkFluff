namespace BarkFluff.Shared.Exceptions.Identity;

public class AccountBlockedException : BaseGrpcException
{
    public override string ErrorCode => "F34D8C22-C075-4E9C-92AD-C893F192D8FA";

    public override string ErrorMessage => nameof(AccountBlockedException);
}
