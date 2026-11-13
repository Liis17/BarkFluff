using System.Net;
using System.Net.Http;
using System.Net.Security;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace BarkFluff.BftProxy;

internal sealed class BftForwarder : IDisposable
{
    private readonly HttpMessageInvoker _client;
    private readonly IReadOnlyDictionary<byte, Uri> _upstreams;
    private readonly ILogger _logger;
    private readonly IHttpForwarder _forwarder;
    private readonly HttpTransformer _transformer;
    private readonly ForwarderRequestConfig _http1 = RequestConfig(HttpVersion.Version11);
    private readonly ForwarderRequestConfig _http2 = RequestConfig(HttpVersion.Version20);

    public BftForwarder(IServiceProvider services, ExitConfiguration configuration, ILogger logger,
        RemoteCertificateValidationCallback? testCertificateValidation = null)
    {
        _upstreams = configuration.Upstreams;
        _logger = logger;
        _forwarder = services.GetRequiredService<IHttpForwarder>();
        _transformer = new BftRequestTransformer(configuration.ProxyId, configuration.OriginSecret);
        _client = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromHours(24),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            SslOptions = testCertificateValidation is null
                ? null
                : new System.Net.Security.SslClientAuthenticationOptions { RemoteCertificateValidationCallback = testCertificateValidation }
        }, disposeHandler: true);
    }

    public async Task ForwardAsync(HttpContext context)
    {
        var feature = context.Features.Get<BftProxyConnectionFeature>();
        if (feature is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!_upstreams.TryGetValue(feature.ServiceId, out var upstream))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var destination = upstream.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var requestConfig = context.Request.Protocol.StartsWith("HTTP/2", StringComparison.Ordinal)
            ? _http2
            : _http1;
        var error = await _forwarder.SendAsync(context, destination, _client, requestConfig, _transformer).ConfigureAwait(false);
        if (error != ForwarderError.None)
        {
            var errorFeature = context.Features.Get<IForwarderErrorFeature>();
            _logger.LogWarning(errorFeature?.Exception, "BFT upstream forwarding failed for {ServiceId} ({ServiceName}) from {ClientIp}; tunnel {TunnelId}, stream {StreamId}, error {ForwarderError}",
                feature.ServiceId, ProxyConfiguration.ServiceName(feature.ServiceId), feature.ClientIp, feature.TunnelId, feature.StreamId, error);
            if (!context.RequestAborted.IsCancellationRequested)
                await feature.ResetUpstreamFailureAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => _client.Dispose();

    private static ForwarderRequestConfig RequestConfig(Version version) => new()
    {
        ActivityTimeout = Timeout.InfiniteTimeSpan,
        AllowResponseBuffering = false,
        Version = version,
        VersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    private sealed class BftRequestTransformer : HttpTransformer
    {
        private readonly string _proxyId;
        private readonly string _originSecret;

        public BftRequestTransformer(string proxyId, string originSecret)
        {
            _proxyId = proxyId;
            _originSecret = originSecret;
        }

        private static readonly string[] RemoveExact = [
            "X-Real-IP", "Forwarded", "CF-Connecting-IP", "True-Client-IP"
        ];

        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest,
            string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken).ConfigureAwait(false);

            var headers = proxyRequest.Headers.Select(pair => pair.Key)
                .Concat(proxyRequest.Content?.Headers.Select(pair => pair.Key) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var header in headers)
            {
                if (RemoveExact.Contains(header, StringComparer.OrdinalIgnoreCase) ||
                    header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
                    header.StartsWith("X-BFT-", StringComparison.OrdinalIgnoreCase))
                {
                    proxyRequest.Headers.Remove(header);
                    proxyRequest.Content?.Headers.Remove(header);
                }
            }

            var feature = httpContext.Features.Get<BftProxyConnectionFeature>()!;
            proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(destinationPrefix, httpContext.Request.Path, httpContext.Request.QueryString);
            proxyRequest.Headers.Host = null;
            proxyRequest.Headers.TryAddWithoutValidation("X-BFT-Client-IP", feature.ClientIp.ToString());
            proxyRequest.Headers.TryAddWithoutValidation("X-BFT-Proxy-Id", _proxyId);
            proxyRequest.Headers.TryAddWithoutValidation("X-BFT-Proxy-Secret", _originSecret);
        }
    }
}
