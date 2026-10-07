namespace BarkFluff.Shared.Exceptions.Messages;

public class ChatTypeMismatchException : BaseGrpcException
{
    public override string ErrorCode => "D0AC27C0-3381-401D-8EF4-E35610C372AB";

    public override string ErrorMessage => "Это действие недоступно для данного типа чата";
}
