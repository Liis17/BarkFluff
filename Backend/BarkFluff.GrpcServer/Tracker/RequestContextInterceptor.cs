using BarkFluff.Shared.Auth;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Text;

namespace BarkFluff.GrpcServer.Tracker;

public class RequestContextInterceptor : Interceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RequestContextInterceptor> _logger;

    public RequestContextInterceptor(IServiceProvider serviceProvider, ILogger<RequestContextInterceptor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var httpContext = context.GetHttpContext();
        var accessor = httpContext.RequestServices.GetRequiredService<IRequestContextAccessor>();

        var metadata = context.RequestHeaders;

        var requestContext = new RequestContext
        {
            DeviceName = GetMetadataValue(metadata, MetadataKeys.DeviceName),
            OperationSystem = GetMetadataValue(metadata, MetadataKeys.OsName),
            AppName = GetMetadataValue(metadata, MetadataKeys.AppName),
            AppVersion = GetMetadataValue(metadata, MetadataKeys.AppVersion),
            DeviceId = GetMetadataValue(metadata, MetadataKeys.DeviceId),
            IpAddress = ResolveIpAddress(metadata, httpContext),
        };

        accessor.Set(requestContext);

        _logger.LogDebug(
            "Входящий gRPC запрос {Method} от устройства: {DeviceName} ({OS}), IP: {IpAddress}, DeviceID: {DeviceId}, Приложение: {AppName} v{AppVersion}",
            context.Method,
            requestContext.DeviceName ?? "Unknown",
            requestContext.OperationSystem ?? "Unknown",
            requestContext.IpAddress ?? "Unknown",
            requestContext.DeviceId ?? "Unknown",
            requestContext.AppName ?? "Unknown",
            requestContext.AppVersion ?? "Unknown"
        );

        return await continuation(request, context);
    }

    /// <summary>
    /// Определяет IP-адрес клиента по нескольким источникам в порядке приоритета:
    /// 1. Заголовок метаданных x-ip-address (явно передан клиентом)
    /// 2. HTTP-заголовок X-Forwarded-For (reverse proxy / load balancer)
    /// 3. HTTP-заголовок X-Real-IP (nginx)
    /// 4. Реальный IP TCP-соединения (RemoteIpAddress)
    /// </summary>
    private string? ResolveIpAddress(Metadata metadata, HttpContext? httpContext)
    {
        // 1. IP из метаданных gRPC (передаётся клиентом через XIpClientInterceptor)
        var clientIp = GetMetadataValue(metadata, MetadataKeys.IpAddress);
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            _logger.LogDebug("IP определён из метаданных клиента: {IpAddress}", clientIp);
            return clientIp;
        }

        // 2. X-Forwarded-For — стандартный заголовок прокси/балансировщика.
        // Формат: "client, proxy1, proxy2" — берём первый (реальный клиент).
        var forwardedFor = httpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstIp))
            {
                _logger.LogDebug("IP определён из X-Forwarded-For: {IpAddress}", firstIp);
                return firstIp;
            }
        }

        // 3. X-Real-IP — заголовок, выставляемый nginx
        var realIp = httpContext?.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(realIp))
        {
            _logger.LogDebug("IP определён из X-Real-IP: {IpAddress}", realIp);
            return realIp;
        }

        // 4. Прямой IP TCP-соединения
        var remoteIp = httpContext?.Connection?.RemoteIpAddress;
        if (remoteIp == null)
            return null;

        // Нормализуем IPv4-mapped IPv6 (::ffff:192.168.1.1 → 192.168.1.1)
        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        _logger.LogDebug("IP определён из соединения (RemoteIpAddress): {IpAddress}", remoteIp);
        return remoteIp.ToString();
    }

    private string? GetMetadataValue(Metadata metadata, string key)
    {
        var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var base64 = entry?.Value;

        if (string.IsNullOrEmpty(base64))
            return null;

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
