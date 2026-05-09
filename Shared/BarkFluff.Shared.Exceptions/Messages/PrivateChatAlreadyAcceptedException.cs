namespace BarkFluff.Shared.Exceptions.Messages;

public class PrivateChatAlreadyAcceptedException : BaseGrpcException
{
    public override string ErrorCode => "5E2D3B6F-7C89-4D2A-8C71-44E6A12F0C20";

    public override string ErrorMessage => "Приватный чат уже принят";
}
