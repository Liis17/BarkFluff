namespace BarkFluff.Shared.Exceptions.Messages;

public class MessageTextTooLongException : BaseGrpcException
{
    public override string ErrorCode => "9F8B5C2A-7F1D-4E5A-9C3B-1F0E2D4A8B6C";

    public override string ErrorMessage => "Текст сообщения превышает максимально допустимую длину (4096 символов)";
}
