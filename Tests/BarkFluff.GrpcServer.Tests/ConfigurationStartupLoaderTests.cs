using System.Net;

using BarkFluff.GrpcServer;
using BarkFluff.Proto.Configuration;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.GrpcServer.Tests;

public class ConfigurationStartupLoaderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Fetches_configuration_only_after_success(int readyAttempt)
    {
        var attempts = 0;
        var waits = new List<TimeSpan>();
        using var client = Client(new Handler(request =>
        {
            Assert.Equal(HttpVersion.Version20, request.Version);
            Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
            Assert.Equal("/internal/configuration/ready", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(++attempts == readyAttempt ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        }));
        var fetched = 0;
        var expected = new GetConfigurationResponse();

        var result = await ConfigurationStartupLoader.LoadAsync(client, () =>
        {
            Assert.Equal(readyAttempt, attempts);
            fetched++;
            return Task.FromResult(expected);
        }, true, duration => { waits.Add(duration); return Task.CompletedTask; });

        Assert.Same(expected, result);
        Assert.Equal(1, fetched);
        Assert.Equal(readyAttempt - 1, waits.Count);
        Assert.All(waits, duration => Assert.Equal(TimeSpan.FromSeconds(5), duration));
    }

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(-1)]
    [InlineData(-2)]
    public async Task Exhaustion_never_fetches_configuration(int failure)
    {
        var attempts = 0;
        var waits = 0;
        using var client = Client(new Handler(_ =>
        {
            attempts++;
            return Failure(failure);
        }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigurationStartupLoader.LoadAsync(
            client, () => throw new Xunit.Sdk.XunitException("Configuration must not be fetched"), true,
            duration => { Assert.Equal(TimeSpan.FromSeconds(5), duration); waits++; return Task.CompletedTask; }));

        Assert.Contains("5 checks", exception.Message);
        Assert.Equal(5, attempts);
        Assert.Equal(4, waits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    public async Task Recovers_from_transport_failure(int failure)
    {
        var attempts = 0;
        using var client = Client(new Handler(_ => ++attempts == 1 ? Failure(failure) : new HttpResponseMessage(HttpStatusCode.OK)));

        await ConfigurationStartupLoader.LoadAsync(client, () => Task.FromResult(new GetConfigurationResponse()), true,
            _ => Task.CompletedTask);

        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(401)]
    [InlineData(302)]
    public async Task Unexpected_status_fails_without_retry(int status)
    {
        var attempts = 0;
        using var client = Client(new Handler(_ => { attempts++; return new HttpResponseMessage((HttpStatusCode)status); }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigurationStartupLoader.LoadAsync(client,
            () => throw new Xunit.Sdk.XunitException("Unexpected fetch"), true,
            _ => throw new Xunit.Sdk.XunitException("Unexpected delay")));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Configuration_failure_does_not_restart_readiness_checks()
    {
        var attempts = 0;
        using var client = Client(new Handler(_ => { attempts++; return new HttpResponseMessage(HttpStatusCode.OK); }));

        await Assert.ThrowsAsync<HttpRequestException>(() => ConfigurationStartupLoader.LoadAsync(client,
            () => throw new HttpRequestException("fetch failed"), true,
            _ => throw new Xunit.Sdk.XunitException("Unexpected retry")));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Actual_http_timeout_is_bounded_to_five_attempts()
    {
        var handler = new BlockingHandler();
        using var client = Client(handler);
        client.Timeout = TimeSpan.FromMilliseconds(50);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigurationStartupLoader.LoadAsync(client,
            () => throw new Xunit.Sdk.XunitException("Unexpected fetch"), true, _ => Task.CompletedTask));

        Assert.Equal(5, handler.Attempts);
    }

    [Fact]
    public async Task Admin_mode_explicitly_skips_manual_validation()
    {
        using var client = Client(new Handler(request =>
        {
            Assert.Equal("?requireValidSettings=false", request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await ConfigurationStartupLoader.LoadAsync(client, () => Task.FromResult(new GetConfigurationResponse()), false);
    }

    [Fact]
    public async Task Uses_real_cleartext_http2_listener()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listen => listen.Protocols = HttpProtocols.Http2));
        await using var app = builder.Build();
        app.MapGet("/internal/configuration/ready", (HttpContext context) =>
        {
            Assert.Equal("HTTP/2", context.Request.Protocol);
            return Results.Ok();
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };

        await ConfigurationStartupLoader.LoadAsync(client, () => Task.FromResult(new GetConfigurationResponse()), true);
    }

    private static HttpResponseMessage Failure(int failure) => failure switch
    {
        -1 => throw new HttpRequestException("simulated network failure"),
        -2 => throw new TaskCanceledException("simulated timeout"),
        _ => new HttpResponseMessage((HttpStatusCode)failure)
    };

    private static HttpClient Client(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://settings:7003/"),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request should have timed out.");
        }
    }
}
