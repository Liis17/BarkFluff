using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using BarkFluff.Status.Configuration;

namespace BarkFluff.Status.Probing;

/// <summary>Один опрос ноды: HTTPS GET /health/ready, таймаут 5 с, тело не больше лимита, без редиректов.</summary>
public sealed class ReadinessProbe : IDisposable
{
    public const int MaxBodyBytes = 4096;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public const string FederationKey = "federation";

    private readonly TimeProvider _time;
    private readonly HttpClient _standard;
    private readonly HttpClient? _federation;

    public ReadinessProbe(StatusOptions options, TimeProvider time)
    {
        _time = time;
        _standard = CreateClient(pin: null);
        _federation = options.FederationSpkiPin is { } pin ? CreateClient(pin) : null;
    }

    public async Task<ProbeOutcome> ProbeAsync(Uri origin, string service, CancellationToken ct)
    {
        var client = service == FederationKey && _federation is not null ? _federation : _standard;
        var started = Stopwatch.GetTimestamp();
        ProbeState state;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/health/ready"))
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await ReadBoundedAsync(response.Content, timeout.Token);
            state = ReadinessClassifier.Classify((int)response.StatusCode, body, _time.GetUtcNow());
            return new ProbeOutcome(state, ElapsedMs(started), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                                              or IOException or AuthenticationException or InvalidOperationException)
        {
            // Сеть, DNS, TLS и таймаут — недоступность. Детали не выходят за пределы класса ошибки.
            return new ProbeOutcome(ProbeState.Down, ElapsedMs(started),
                exception.GetType().Name);
        }
    }

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static HttpClient CreateClient(string? pin)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = Timeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        if (pin is not null)
        {
            // Только для Federation: цепочка не проверяется, принимается только сертификат с нужным SPKI.
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => FederationPin.Accepts(pin, certificate),
            };
        }
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaxBodyBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
                return Encoding.UTF8.GetString(buffer, 0, total);
            total += read;
        }
        return null;
    }

    public void Dispose()
    {
        _standard.Dispose();
        _federation?.Dispose();
    }
}

public sealed record ProbeOutcome(ProbeState State, long LatencyMs, string? ErrorClass);
