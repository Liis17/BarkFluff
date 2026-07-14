namespace BarkFluff.Shared.Exceptions.Bots;

public class BotStorageQuotaExceededException : BaseGrpcException
{
    public override string ErrorCode => "DFB49E4B-1933-4FD4-9C5B-74D3AA4E67AF";

    public override string ErrorMessage => "Превышена квота хранилища бота (1 ГБ)";
}
