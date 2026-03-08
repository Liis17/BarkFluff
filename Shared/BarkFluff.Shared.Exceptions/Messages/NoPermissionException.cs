namespace BarkFluff.Shared.Exceptions.Messages;

public class NoPermissionException : BaseGrpcException
{
    public override string ErrorCode => "AD582481-BAA9-4715-B3B9-825C886DFEC3";

    public override string ErrorMessage => "У вас нет доступа для этого действия";
}