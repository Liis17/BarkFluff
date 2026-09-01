namespace BarkFluff.Shared.Exceptions.Messages;

public class ClientOperationIdNotValidException : BaseGrpcException
{
    public override string ErrorCode => "8C426DE2-CB07-485B-A80E-7C9C866E8AE4";

    public override string ErrorMessage => "Client operation id должен быть UUID";
}
