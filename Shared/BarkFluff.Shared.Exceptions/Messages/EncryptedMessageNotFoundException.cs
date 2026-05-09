namespace BarkFluff.Shared.Exceptions.Messages;

public class EncryptedMessageNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "FA3C1B6E-5D9F-4A48-AB12-DD3F62E3C481";

    public override string ErrorMessage => "Шифрованное сообщение не найдено";
}
