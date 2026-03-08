namespace BarkFluff.Shared.Exceptions.Messages;

public class FileNotSupportedException : BaseGrpcException
{
    public override string ErrorCode => "DE755405-706A-4471-B3CA-5E0A3DCF8566";

    public override string ErrorMessage => "Переданный файл не поддерживается для отправки в сообщении";
}