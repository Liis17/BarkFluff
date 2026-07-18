using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Federation;

public class FederationServerBlockedException : BaseGrpcException
{
    public override string ErrorCode => "213152EB-1B6C-40C5-BA1D-0FD88DF1752A";
    public override string ErrorMessage => "Нода заблокирована";
    public override StatusCode StatusCode => StatusCode.PermissionDenied;
}
