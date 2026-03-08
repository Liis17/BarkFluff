namespace BarkFluff.Shared.Exceptions.Identity;

public class UsernameOrEmailIsEmptyException : BaseGrpcException
{
    public override string ErrorCode => "84EC96DA-2A1A-499E-ACED-C1444E07E0E6";

    public override string ErrorMessage => "Передан пустое имя пользователя или емайл";
}