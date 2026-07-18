using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Federation;

// docs/rearch/02-trust-and-certs.md, "Проблемы и открытые вопросы": диагностика рассинхрона часов
// между нодами. Сообщение несёт серверное время (Изменение 3, step-1.3-xfed-signing.md).
public class ClockSkewDetectedException : BaseGrpcException
{
    public override string ErrorCode => "B29A587C-095D-436D-BFDC-FD94D7203C23";

    private readonly string _message;

    public ClockSkewDetectedException() : this("Рассинхронизация часов между нодами")
    {
    }

    public ClockSkewDetectedException(string message)
    {
        _message = message;
    }

    public override string ErrorMessage => _message;
    public override StatusCode StatusCode => StatusCode.Unauthenticated;
}
