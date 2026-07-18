using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Federation;

public class FederationPeerNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "52EF0116-D1CB-4CDD-A5DD-99DD900D729B";
    public override string ErrorMessage => "Нода не найдена в KnownServers";
    public override StatusCode StatusCode => StatusCode.NotFound;
}
