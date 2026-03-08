using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Auth;

public class JwtClientInterceptor : Interceptor
{
    private readonly string _token;

    public JwtClientInterceptor(string token)
    {
        _token = token;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add(MetadataKeys.Token, _token);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}