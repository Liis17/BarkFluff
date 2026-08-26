using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Identity;

public class IdentityProtectionUnavailableException : BaseGrpcException
{
    public override string ErrorCode => "A7B3D2F1-4C6E-4E6D-8A6B-2F0A9C7D5E11";

    public override string ErrorMessage => "Защита входа временно недоступна. Повторите попытку позже";

    public override StatusCode StatusCode => StatusCode.Unavailable;
}
