namespace BarkFluff.Shared.Exceptions.Messages;

public class TooManyPinnedMessagesException : BaseGrpcException
{
    public override string ErrorCode => "F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23";

    public override string ErrorMessage => "Превышено максимальное количество закреплённых сообщений в чате (100)";
}
