using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Identity;

public class IdentityRateLimitExceededException : BaseGrpcException
{
    public override string ErrorCode => "7D1CBF0E-2C85-4A2A-9B2D-6B2A6CF5A1E2";

    public override string ErrorMessage => "Слишком много запросов. Повторите попытку позже";

    public override StatusCode StatusCode => StatusCode.ResourceExhausted;
}
