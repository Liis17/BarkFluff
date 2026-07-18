using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Federation;

public class FederationNotConfiguredException : BaseGrpcException
{
    public override string ErrorCode => "AC6E038B-96E8-417A-A91B-5DEF9E1ADB4D";
    public override string ErrorMessage => "Федерация не сконфигурирована на этой ноде";
    public override StatusCode StatusCode => StatusCode.FailedPrecondition;
}
