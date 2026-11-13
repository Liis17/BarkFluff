namespace BarkFluff.Shared.Exceptions.Users;

public class UserUnavailableException : BaseGrpcException
{
    public override string ErrorCode => "6BC0E7A1-5117-4910-9717-1784E6921F6F";

    public override string ErrorMessage => nameof(UserUnavailableException);
}
