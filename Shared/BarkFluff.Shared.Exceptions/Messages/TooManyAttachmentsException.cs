namespace BarkFluff.Shared.Exceptions.Messages;

public class TooManyAttachmentsException : BaseGrpcException
{
    public override string ErrorCode => "B3A4D7F2-5C6E-4A8B-9D1F-3E2C7B8A0F4D";

    public override string ErrorMessage => "Превышено максимальное количество вложений в одном сообщении (10)";
}
