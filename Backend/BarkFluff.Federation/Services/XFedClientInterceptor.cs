using BarkFluff.GrpcServer.Metrics;

using Google.Protobuf;

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Federation.Services;

// Подпись исходящих S2S-запросов (по образцу JwtClientInterceptor в Shared/BarkFluff.Shared.Auth).
// Сериализует запрос IMessage.ToByteArray() — тот же кодовый путь, что у wire-marshaller'а
// (docs/rearch/phase-1/step-1.3-xfed-signing.md, Изменение 4), поэтому байты идентичны тому,
// что получит и захеширует XFedRawBytesMiddleware на стороне адресата.
public class XFedClientInterceptor : Interceptor
{
    private readonly IConfiguration _configuration;
    private readonly ActiveSigningKeyCache _keyCache;
    private readonly MetricsCollector _metrics;
    private readonly string _destinationServerName;

    public XFedClientInterceptor(IConfiguration configuration, ActiveSigningKeyCache keyCache, MetricsCollector metrics, string destinationServerName)
    {
        _configuration = configuration;
        _keyCache = keyCache;
        _metrics = metrics;
        _destinationServerName = destinationServerName;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithSignature(request, context));
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithSignature(request, context));
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithSignature(request, context));
    }

    // AsyncClientStreamingCall/AsyncDuplexStreamingCall не переопределяем: все S2S-RPC v1 имеют
    // унарные запросы (стримы только в ответах — FetchFile, SubscribePresence), см. step-1.3.

    private ClientInterceptorContext<TRequest, TResponse> WithSignature<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var origin = _configuration["Federation:ServerName"] ?? string.Empty;
        var activeKey = _keyCache.Current
            ?? throw new InvalidOperationException("Нет активного signing-ключа для подписи исходящего S2S-запроса");

        var requestBytes = ((IMessage)request).ToByteArray();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var methodFullName = context.Method.FullName;

        var canonical = XFedCanonicalString.Build(origin, _destinationServerName, timestampMs, methodFullName, requestBytes);
        var signature = SigningKeyService.SignRaw(activeKey.PrivateKeySeed, canonical);

        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add(XFedHeaders.Origin, origin);
        metadata.Add(XFedHeaders.Destination, _destinationServerName);
        metadata.Add(XFedHeaders.Timestamp, timestampMs.ToString());
        metadata.Add(XFedHeaders.KeyId, activeKey.KeyId);
        metadata.Add(XFedHeaders.Signature, Convert.ToBase64String(signature));

        _metrics.Increment("s2s_requests_out");

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(metadata));
    }
}
