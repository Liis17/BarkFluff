namespace BarkFluff.Shared.Exceptions.Navigator;

public class NameEmptyException : BaseGrpcException
{
    public override string ErrorCode => "1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D";
    public override string ErrorMessage => "Name не может быть пустым";
} 