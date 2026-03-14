namespace BarkFluff.Shared.Exceptions.Identity;

public class UsernameReservedException : BaseGrpcException
{
    public override string ErrorCode => "A3F1B2C4-7D8E-4F5A-9B6C-1E2D3F4A5B6C";

    public override string ErrorMessage => "Это имя пользователя зарезервировано и не может быть использовано";
}
