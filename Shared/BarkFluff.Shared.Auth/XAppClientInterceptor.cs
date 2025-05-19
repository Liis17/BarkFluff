using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Auth;

public class XAppClientInterceptor : Interceptor
{
    private readonly string _appName;
    private readonly string _appVersion;

    public XAppClientInterceptor(string appName, string appVersion)
    {
        this._appName = appName;
        this._appVersion = appVersion;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        
        metadata.Add(MetadataKeys.AppName, _appName);
        metadata.Add(MetadataKeys.AppVersion, _appVersion);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}