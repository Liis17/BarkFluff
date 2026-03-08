using Grpc.Core;
using Grpc.Core.Interceptors;

using System.Text;

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

        var appName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_appName));
        var appVersion = Convert.ToBase64String(Encoding.UTF8.GetBytes(_appVersion));

        metadata.Add(MetadataKeys.AppName, appName);
        metadata.Add(MetadataKeys.AppVersion, appVersion);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}