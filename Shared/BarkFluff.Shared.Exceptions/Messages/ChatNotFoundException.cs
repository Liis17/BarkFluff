namespace BarkFluff.Shared.Exceptions.Messages;

public class ChatNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "7506386A-8940-4F3B-87B8-315DD0A7AB08";

    public override string ErrorMessage => "Чат не найден";
}