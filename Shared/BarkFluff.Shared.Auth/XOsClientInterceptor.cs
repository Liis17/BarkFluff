using Grpc.Core;
using Grpc.Core.Interceptors;

using System.Text;

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

        var osName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_osName));

        metadata.Add(MetadataKeys.OsName, osName);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}