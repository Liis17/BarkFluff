using System.Text;
using BarkFluff.Shared.Auth;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.GrpcServer.Tracker;

public class RequestContextInterceptor : Interceptor
{
    private readonly IServiceProvider _serviceProvider;

    public RequestContextInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var httpContext = context.GetHttpContext();
        var requestContext = httpContext.RequestServices.GetRequiredService<RequestContext>();
        
        var metadata = context.RequestHeaders;
        requestContext.DeviceName = GetMetadataValue(metadata, MetadataKeys.DeviceName);
        requestContext.OperationSystem = GetMetadataValue(metadata, MetadataKeys.OsName);
        requestContext.AppName = GetMetadataValue(metadata, MetadataKeys.AppName);
        requestContext.AppVersion = GetMetadataValue(metadata, MetadataKeys.AppVersion);
        
        requestContext.IpAddress = GetMetadataValue(metadata, MetadataKeys.IpAddress) 
                                   ?? httpContext?.Connection?.RemoteIpAddress?.ToString();
        
        return await continuation(request, context);
    }
    
    private string? GetMetadataValue(Metadata metadata, string key)
    {
        var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var base64 = entry?.Value;

        if (string.IsNullOrEmpty(base64))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

}