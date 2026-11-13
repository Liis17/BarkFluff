namespace BarkFluff.Shared.Exceptions.Identity;

public class AccountPendingDeletionException : BaseGrpcException
{
    public override string ErrorCode => "8E6F0F30-98AF-4F96-A63B-20D52AEEFB6A";

    public override string ErrorMessage => nameof(AccountPendingDeletionException);
}
