using Grpc.Core;
using Grpc.Core.Interceptors;

using System.Text;

namespace BarkFluff.Shared.Auth;

public class XIpClientInterceptor : Interceptor
{
    private readonly string _ipAddr;

    public XIpClientInterceptor(string ip)
    {
        _ipAddr = ip;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();

        var ipAddress = Convert.ToBase64String(Encoding.UTF8.GetBytes(_ipAddr));

        metadata.Add(MetadataKeys.IpAddress, ipAddress);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}