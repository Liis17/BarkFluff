namespace BarkFluff.Shared.Exceptions.Identity;

public class UsernameExistException : BaseGrpcException
{
    public override string ErrorCode => "DB157CD8-98A3-4A35-9857-33821813D422";

    public override string ErrorMessage => "Пользователь с таким именем пользователя существует";
}