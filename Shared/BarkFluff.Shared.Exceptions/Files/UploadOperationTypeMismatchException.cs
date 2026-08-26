namespace BarkFluff.Shared.Exceptions.Files;

public class UploadOperationTypeMismatchException : BaseGrpcException
{
    public override string ErrorCode => "4FCAC2EF-4915-43D6-BB2B-74B29387858F";

    public override string ErrorMessage => "Тип файла не совпадает с ранее созданной операцией загрузки";
}
