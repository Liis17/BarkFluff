using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BarkFluff.Status.Configuration;
using BarkFluff.Status.Probing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BarkFluff.Status.Tests;

/// <summary>Реальный HTTPS/HTTP/2 через Kestrel на localhost с самоподписанным сертификатом.</summary>
public sealed class ReadinessProbeTests : IAsyncLifetime
{
    private WebApplication? _app;
    private Uri _origin = new("https://localhost:1");
    private string _pinOfServer = string.Empty;
    private int _code = 200;
    private string _body = string.Empty;
    private TimeSpan _delay = TimeSpan.Zero;
    private int _redirect;
    private string _protocol = string.Empty;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private async Task StartAsync()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, "pw"), "pw", X509KeyStorageFlags.Exportable);
        _pinOfServer = Convert.ToBase64String(SHA256.HashData(pfx.PublicKey.ExportSubjectPublicKeyInfo()));

        var port = Random.Shared.Next(20000, 40000);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenLocalhost(port, listen => listen.UseHttps(pfx));
        });
        _app = builder.Build();
        _app.MapGet("/health/ready", async (HttpContext context, CancellationToken ct) =>
        {
            if (_redirect != 0)
            {
                context.Response.Redirect("/elsewhere", permanent: false);
                return;
            }
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct);
            _protocol = context.Request.Protocol;
            context.Response.StatusCode = _code;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(_body, ct);
        });
        await _app.StartAsync();
        _origin = new Uri($"https://localhost:{port}");
    }

    private void Respond(int code, string body)
    {
        _code = code;
        _body = body;
    }

    private static string Snapshot(string status) =>
        $$"""{"status":"{{status}}","checkedAtUtc":"{{DateTimeOffset.UtcNow.UtcDateTime:O}}","checks":[],"instanceId":"x","extra":"ignored"}""";

    private static StatusOptions Options(string? pin) => new()
    {
        NodeDomain = "example.test",
        ServiceOrigins = new Dictionary<string, Uri>(),
        FederationSpkiPin = pin,
        DatabasePath = "unused.db",
    };

    [Fact]
    public async Task HealthyIsUpOverHttp2()
    {
        await StartAsync();
        Respond(200, Snapshot("healthy"));
        using var probe = new ReadinessProbe(Options(pin: _pinOfServer), TimeProvider.System);

        var outcome = await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None);

        Assert.Equal(ProbeState.Up, outcome.State);
        Assert.Null(outcome.ErrorClass);
        Assert.Equal("HTTP/2", _protocol);
    }

    [Fact]
    public async Task ServerDownWith503IsDown()
    {
        await StartAsync();
        Respond(503, Snapshot("down"));
        using var probe = new ReadinessProbe(Options(pin: _pinOfServer), TimeProvider.System);

        var outcome = await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None);

        Assert.Equal(ProbeState.Down, outcome.State);
        Assert.Null(outcome.ErrorClass);
    }

    [Fact]
    public async Task SelfSignedCertificateIsRejectedWithoutPin()
    {
        await StartAsync();
        Respond(200, Snapshot("healthy"));
        using var probe = new ReadinessProbe(Options(pin: null), TimeProvider.System);

        // Обычная проверка сертификата: самоподписанный сертификат не доверен, обхода нет.
        var outcome = await probe.ProbeAsync(_origin, "messages", CancellationToken.None);

        Assert.Equal(ProbeState.Down, outcome.State);
        Assert.NotNull(outcome.ErrorClass);
    }

    [Fact]
    public async Task FederationWithMatchingPinIsUpWithoutCaTrust()
    {
        await StartAsync();
        Respond(200, Snapshot("degraded"));
        using var probe = new ReadinessProbe(Options(pin: _pinOfServer), TimeProvider.System);

        var outcome = await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None);

        Assert.Equal(ProbeState.Degraded, outcome.State);
    }

    [Fact]
    public async Task FederationWithWrongPinFailsClosed()
    {
        await StartAsync();
        Respond(200, Snapshot("healthy"));
        var wrongPin = Convert.ToBase64String(SHA256.HashData("another key"u8.ToArray()));
        using var probe = new ReadinessProbe(Options(pin: wrongPin), TimeProvider.System);

        Assert.Equal(ProbeState.Down, (await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None)).State);
    }

    [Fact]
    public async Task PinDoesNotApplyToOtherServices()
    {
        await StartAsync();
        Respond(200, Snapshot("healthy"));
        var wrongPin = Convert.ToBase64String(SHA256.HashData("another key"u8.ToArray()));
        using var probe = new ReadinessProbe(Options(pin: wrongPin), TimeProvider.System);

        // Пин задан, но сертификат не федерационный: обычная проверка CA, самоподписанный отказ.
        Assert.Equal(ProbeState.Down, (await probe.ProbeAsync(_origin, "messages", CancellationToken.None)).State);
    }

    [Fact]
    public async Task RedirectIsNotFollowedAndIsDown()
    {
        await StartAsync();
        _redirect = 1;
        using var probe = new ReadinessProbe(Options(pin: _pinOfServer), TimeProvider.System);

        Assert.Equal(ProbeState.Down, (await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None)).State);
        _redirect = 0;
    }

    [Fact]
    public async Task OversizedBodyIsDown()
    {
        await StartAsync();
        Respond(200, Snapshot("healthy") + new string(' ', ReadinessProbe.MaxBodyBytes));
        using var probe = new ReadinessProbe(Options(pin: _pinOfServer), TimeProvider.System);

        Assert.Equal(ProbeState.Down, (await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None)).State);
    }

    [Fact]
    public async Task SlowResponseTimesOutAsDown()
    {
        await StartAsync();
        Respond(200, Snapshot("healthy"));
        _delay = TimeSpan.FromSeconds(ReadinessProbe.Timeout.TotalSeconds + 3);
        using var probe = new ReadinessProbe(Options(pin: _pinOfServer), TimeProvider.System);

        var outcome = await probe.ProbeAsync(_origin, ReadinessProbe.FederationKey, CancellationToken.None);

        Assert.Equal(ProbeState.Down, outcome.State);
        _delay = TimeSpan.Zero;
    }
}
