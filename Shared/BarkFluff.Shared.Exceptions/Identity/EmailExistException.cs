namespace BarkFluff.Shared.Exceptions.Identity;

public class EmailExistException : BaseGrpcException
{
    public override string ErrorCode => "7599F3F1-C2EC-4D05-BF38-A1A60D40BA4E";

    public override string ErrorMessage => "Пользователь с таким емейлом зарегристирован";
}