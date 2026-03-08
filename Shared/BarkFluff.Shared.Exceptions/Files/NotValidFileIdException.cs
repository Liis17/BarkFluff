namespace BarkFluff.Shared.Exceptions.Files;

public class NotValidFileIdException : BaseGrpcException
{
    public override string ErrorCode => "D10BD126-48EA-4D11-9CFF-4C2FDD6F9899";

    public override string ErrorMessage => "Неверный формат идентификатора файла. Он должен быть guid";
}