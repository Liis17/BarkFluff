using BarkFluff.Shared.Exceptions;

using Grpc.Core;

namespace BarkFluff.Users.Persistence.Services;

public sealed class PrekeyException : BaseGrpcException
{
    private readonly string _message;

    public PrekeyException(StatusCode statusCode, string message)
    {
        StatusCode = statusCode;
        _message = message;
    }

    public override StatusCode StatusCode { get; }

    public override string ErrorMessage => _message;
}
