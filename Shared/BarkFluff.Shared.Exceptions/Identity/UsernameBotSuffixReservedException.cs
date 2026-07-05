namespace BarkFluff.Shared.Exceptions.Identity;

public class UsernameBotSuffixReservedException : BaseGrpcException
{
    public override string ErrorCode => "B0741D3A-6C2E-4E9F-9A1B-2F5C7D8E0A11";

    public override string ErrorMessage => "Имя пользователя, заканчивающееся на «bot», зарезервировано для ботов";
}
