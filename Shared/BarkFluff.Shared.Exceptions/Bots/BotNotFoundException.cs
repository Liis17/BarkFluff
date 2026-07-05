namespace BarkFluff.Shared.Exceptions.Bots;

public class BotNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "4F8A2D1C-9B3E-47A6-8C5D-1E7F0B2A9D34";

    public override string ErrorMessage => "Бот не найден";
}
