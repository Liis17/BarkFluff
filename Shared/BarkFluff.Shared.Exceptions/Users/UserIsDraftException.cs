namespace BarkFluff.Shared.Exceptions.Users;

public class UserIsDraftException : BaseGrpcException
{
    public override string ErrorCode => "91D75288-8314-4658-AA0E-EC1D01779D58";

    public override string ErrorMessage => "Пользователь с такими параметрами есть, но он не подтвержден";
}