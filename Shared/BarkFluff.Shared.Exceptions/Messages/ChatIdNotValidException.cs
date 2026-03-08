namespace BarkFluff.Shared.Exceptions.Messages;

public class ChatIdNotValidException : BaseGrpcException
{
    public override string ErrorCode => "91CB4758-151F-4BF7-8C6D-435923CDF1AF";

    public override string ErrorMessage => "Был передан невалидный chat id. Он должен быть guid";
}