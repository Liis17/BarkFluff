using Grpc.Core;
using Grpc.Core.Interceptors;

using System.Text;

namespace BarkFluff.Shared.Auth;

public class XDeviceIdInterceptor : Interceptor
{
    private readonly string _deviceId;

    public XDeviceIdInterceptor(string deviceId)
    {
        _deviceId = deviceId;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();

        var osName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_deviceId));

        metadata.Add(MetadataKeys.DeviceId, osName);
        

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}