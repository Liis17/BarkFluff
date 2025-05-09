using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Auth;

public class XOsClientInterceptor : Interceptor
{
    private readonly string _osName;

    public XOsClientInterceptor(string osName)
    {
        _osName = osName;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add("x-os-name", _osName);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}