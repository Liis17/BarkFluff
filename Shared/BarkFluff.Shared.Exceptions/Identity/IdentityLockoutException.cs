using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Identity;

public class IdentityLockoutException : BaseGrpcException
{
    public override string ErrorCode => "B95A5B58-6A7F-43A2-A9B9-D9D8F8B4B1E4";

    public override string ErrorMessage => "Временно заблокировано. Повторите попытку позже";

    public override StatusCode StatusCode => StatusCode.ResourceExhausted;
}
