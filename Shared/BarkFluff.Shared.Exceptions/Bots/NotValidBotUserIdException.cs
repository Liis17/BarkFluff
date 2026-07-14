namespace BarkFluff.Shared.Exceptions.Bots;

public class NotValidBotUserIdException : BaseGrpcException
{
    public override string ErrorCode => "68D641B8-A231-40C3-AFAB-7ED0C08D0BD3";

    public override string ErrorMessage => "Некорректный идентификатор бота";
}
