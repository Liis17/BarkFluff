namespace BarkFluff.Shared.Exceptions.Files;

public class ClientOperationIdNotValidException : BaseGrpcException
{
    public override string ErrorCode => "670F9884-187C-444B-873B-1B7FB00E3DD7";

    public override string ErrorMessage => "Client operation id должен быть UUID";
}
