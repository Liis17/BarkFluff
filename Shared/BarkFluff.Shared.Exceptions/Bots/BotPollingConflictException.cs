namespace BarkFluff.Shared.Exceptions.Bots;

public class BotPollingConflictException : BaseGrpcException
{
    public override string ErrorCode => "79870B49-B14A-43D6-B693-A767F8F3ECAF";

    public override string ErrorMessage => "У бота уже есть активный поток получения update'ов";
}
