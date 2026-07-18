using Grpc.Core;

namespace BarkFluff.Shared.Exceptions;

public class BaseGrpcException : Exception
{
    public virtual string ErrorCode { get; } = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";

    public virtual string ErrorMessage { get; } = "Неизвестная ошибка";

    // Дефолт сохраняет текущее поведение ServerExceptionInterceptor для всех существующих
    // исключений; переопределяется там, где нужен не FailedPrecondition (см. XFed, Federation).
    public virtual StatusCode StatusCode => StatusCode.FailedPrecondition;
}