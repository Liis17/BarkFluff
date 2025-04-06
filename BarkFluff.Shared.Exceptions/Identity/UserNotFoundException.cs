namespace BarkFluff.Shared.Exceptions.Identity;

public class UserNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "A4DAB334-1067-4838-A782-C4257DC838F7";

    public override string ErrorMessage => "Пользователь не найден";
}