namespace BarkFluff.GrpcServer.Tracker;

public interface IRequestContextAccessor
{
    RequestContext Current { get; }

    void Set(RequestContext context);
}

internal sealed class RequestContextAccessor : IRequestContextAccessor
{
    private RequestContext? _context;

    public RequestContext Current =>
        _context ?? throw new InvalidOperationException(
            "RequestContext не инициализирован. " +
            "Убедитесь, что RequestContextInterceptor выполняется до разрешения RequestContext.");

    public void Set(RequestContext context)
    {
        if (_context != null)
        {
            throw new InvalidOperationException("RequestContext уже инициализирован для этого scope.");
        }

        _context = context;
    }
}
