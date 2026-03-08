namespace BarkFluff.Shared.Exceptions.Messages;

public class MessageNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "C0EEF1D9-BE99-4645-9EBD-95FF36A2BF45";

    public override string ErrorMessage => "Сообщение не найдено";
}