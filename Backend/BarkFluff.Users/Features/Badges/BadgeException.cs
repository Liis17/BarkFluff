using BarkFluff.Shared.Exceptions;

using Grpc.Core;

namespace BarkFluff.Users.Features.Badges;

public sealed class BadgeException : BaseGrpcException
{
    private readonly string _message;

    public BadgeException(StatusCode statusCode, string message)
    {
        StatusCode = statusCode;
        _message = message;
    }

    public override StatusCode StatusCode { get; }

    public override string ErrorMessage => _message;
}
