namespace BarkFluff.Shared.Exceptions.Messages;

public class NoAccessToChatException : BaseGrpcException
{
    public override string ErrorCode => "604DD334-0484-4C6B-8113-354B9D2FDF2A";

    public override string ErrorMessage => "Нет доступа к этому чату";
}