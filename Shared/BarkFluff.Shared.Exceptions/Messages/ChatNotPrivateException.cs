namespace BarkFluff.Shared.Exceptions.Messages;

public class ChatNotPrivateException : BaseGrpcException
{
    public override string ErrorCode => "9F8E2C84-3B4D-4A7A-8F1C-5B2C0E77AC11";

    public override string ErrorMessage => "Чат не является приватным";
}
