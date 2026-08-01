namespace BarkFluff.Shared.Exceptions.Messages;

public class ChatNotRegularException : BaseGrpcException
{
    public override string ErrorCode => "CF4654C7-856B-480A-BCE1-8A76B1C328C8";

    public override string ErrorMessage => "Черновики доступны только для обычных чатов";
}
