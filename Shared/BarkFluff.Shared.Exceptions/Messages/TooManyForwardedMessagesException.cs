namespace BarkFluff.Shared.Exceptions.Messages;

public class TooManyForwardedMessagesException : BaseGrpcException
{
    public override string ErrorCode => "C4B9E2D1-7A63-4E58-9F0C-2D8B5A1E6047";

    public override string ErrorMessage => "Превышено максимальное количество пересылаемых сообщений (20)";
}
