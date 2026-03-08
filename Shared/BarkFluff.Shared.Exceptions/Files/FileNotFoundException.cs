namespace BarkFluff.Shared.Exceptions.Files;

public class FileNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "91E25C73-FC80-43C1-893D-F26F39726F03";

    public override string ErrorMessage => "Файл не найден";
}