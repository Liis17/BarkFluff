using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Federation;

// Общее исключение для отказов XFed (docs/rearch/phase-1/step-1.3-xfed-signing.md), не требующих
// собственного именованного кода: отсутствующие заголовки, чужой destination, неизвестный ключ,
// невалидная подпись. Parameterless-конструктор обязателен — ExceptionClientInterceptor находит
// все BaseGrpcException через Activator.CreateInstance(t) по сборке.
public class XFedUnauthenticatedException : BaseGrpcException
{
    public override string ErrorCode => "34AD5E00-4852-435F-89B0-96B6CE99834C";

    private readonly string _message;

    public XFedUnauthenticatedException() : this("Проверка подписи S2S-запроса не пройдена")
    {
    }

    public XFedUnauthenticatedException(string message)
    {
        _message = message;
    }

    public override string ErrorMessage => _message;
    public override StatusCode StatusCode => StatusCode.Unauthenticated;
}
