using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Exceptions.Federation;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Host;

// Единая точка проверки XFed (docs/rearch/02-trust-and-certs.md, "Подпись каждого S2S-запроса";
// docs/rearch/phase-1/step-1.3-xfed-signing.md). Вешается только на FederationS2SApiService
// (AddServiceOptions) — FederationInternalApi остаётся под XAuth. Бросает BaseGrpcException-потомков:
// глобальный ServerExceptionInterceptor (внешний в цепочке — см. Context7 aspnetcore.docs,
// "globally-configured interceptors run before service-specific ones") конвертирует их в RpcException
// с нужным StatusCode.
public class XFedServerInterceptor : Interceptor
{
    private const string ExemptMethodSuffix = "/GetServerKeys";

    private readonly FederationContext _context;
    private readonly IConfiguration _configuration;
    private readonly FederationSwitch _switch;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<XFedServerInterceptor> _logger;
    private readonly ServerResolver _resolver;
    private readonly IDiscoveryTriggerRateLimiter _rateLimiter;

    public XFedServerInterceptor(
        FederationContext context,
        IConfiguration configuration,
        FederationSwitch federationSwitch,
        MetricsCollector metrics,
        ILogger<XFedServerInterceptor> logger,
        ServerResolver resolver,
        IDiscoveryTriggerRateLimiter rateLimiter)
    {
        _context = context;
        _configuration = configuration;
        _switch = federationSwitch;
        _metrics = metrics;
        _logger = logger;
        _resolver = resolver;
        _rateLimiter = rateLimiter;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureActive();

        if (!IsExempt(context.Method))
            await ValidateAsync(context);

        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureActive();

        if (!IsExempt(context.Method))
            await ValidateAsync(context);

        await continuation(request, responseStream, context);
    }

    // P1-04/P1-05: единый выключатель входящего S2S. Применяется ДО IsExempt — покрывает и bootstrap
    // GetServerKeys, поэтому при выключенной/несконфигурированной ноде ключи не отдаются.
    private void EnsureActive()
    {
        if (!_switch.IsActive)
            throw new FederationNotConfiguredException();
    }

    private static bool IsExempt(string method) => method.EndsWith(ExemptMethodSuffix, StringComparison.Ordinal);

    private async Task ValidateAsync(ServerCallContext context)
    {
        var headers = context.RequestHeaders;

        var origin = GetHeader(headers, XFedHeaders.Origin);
        var destination = GetHeader(headers, XFedHeaders.Destination);
        var timestampRaw = GetHeader(headers, XFedHeaders.Timestamp);
        var keyId = GetHeader(headers, XFedHeaders.KeyId);
        var signatureBase64 = GetHeader(headers, XFedHeaders.Signature);

        if (origin == null || destination == null || timestampRaw == null || keyId == null || signatureBase64 == null)
            throw new XFedUnauthenticatedException("Отсутствуют обязательные XFed-заголовки");

        var ownServerName = _configuration["Federation:ServerName"];
        if (string.IsNullOrWhiteSpace(ownServerName))
            throw new FederationNotConfiguredException();

        if (!string.Equals(destination, ownServerName, StringComparison.OrdinalIgnoreCase))
            throw new XFedUnauthenticatedException("Запрос адресован другой ноде");

        if (!long.TryParse(timestampRaw, out var timestampMs))
            throw new XFedUnauthenticatedException("Некорректный формат x-bf-timestamp");

        var windowSeconds = 300;
        var windowConfig = _configuration["Federation:SignatureWindowSeconds"];
        if (!string.IsNullOrWhiteSpace(windowConfig) && int.TryParse(windowConfig, out var parsedWindow))
            windowSeconds = parsedWindow;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - timestampMs) > windowSeconds * 1000L)
        {
            _metrics.Increment("s2s_clock_skew_rejections");
            throw new ClockSkewDetectedException($"Рассинхронизация часов: серверное время {DateTimeOffset.UtcNow:O}");
        }

        var knownKey = await _context.KnownServerKeys
            .FirstOrDefaultAsync(k => k.ServerName == origin && k.KeyId == keyId);

        var keyUsable = knownKey != null && knownKey.RevokedAt == null && (knownKey.ExpiredAt == null || knownKey.ExpiredAt > DateTime.UtcNow);

        // Discovery-на-лету (docs/rearch/03-discovery.md, "Политика обновления"): неизвестный
        // origin/key_id → резолв → повторная проверка один раз. Rate-limit per-server защищает
        // от флуда случайными key_id.
        if (!keyUsable && await _rateLimiter.TryTriggerAsync(origin))
        {
            await _resolver.ResolveAsync(origin);

            knownKey = await _context.KnownServerKeys
                .FirstOrDefaultAsync(k => k.ServerName == origin && k.KeyId == keyId);
            keyUsable = knownKey != null && knownKey.RevokedAt == null && (knownKey.ExpiredAt == null || knownKey.ExpiredAt > DateTime.UtcNow);
        }

        if (!keyUsable)
            throw new XFedUnauthenticatedException("Неизвестный или истёкший ключ пира");

        var httpContext = context.GetHttpContext();
        if (httpContext.Items[XFedRawBytesMiddleware.ItemsKey] is not byte[] requestBytes)
            throw new XFedUnauthenticatedException("Не удалось получить сырые байты запроса");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            throw new XFedUnauthenticatedException("Некорректный формат x-bf-signature");
        }

        var canonical = XFedCanonicalString.Build(origin, destination, timestampMs, context.Method, requestBytes);

        if (!SigningKeyService.Verify(knownKey!.PublicKey, canonical, signature))
        {
            _metrics.Increment("s2s_signature_failures");
            _logger.LogWarning("XFed: подпись не прошла проверку, origin={Origin}", origin);
            throw new XFedUnauthenticatedException("Подпись не прошла проверку");
        }

        var server = await _context.KnownServers.FirstOrDefaultAsync(s => s.ServerName == origin);
        if (server?.Status == KnownServerStatus.Blocked)
            throw new FederationServerBlockedException();

        context.UserState["xfed-origin"] = origin;
        _metrics.Increment("s2s_requests_in");
    }

    private static string? GetHeader(Metadata headers, string key)
    {
        foreach (var entry in headers)
        {
            if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }
}
