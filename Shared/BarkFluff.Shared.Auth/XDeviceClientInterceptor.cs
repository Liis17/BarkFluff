using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Shared.Auth;

public class XDeviceClientInterceptor : Interceptor
{
    private readonly string _deviceName;

    public XDeviceClientInterceptor(string deviceName)
    {
        _deviceName = deviceName;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add("x-device-name", _deviceName);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}