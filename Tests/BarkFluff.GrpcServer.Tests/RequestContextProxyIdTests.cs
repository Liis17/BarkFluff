using System.Collections.Concurrent;

using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Tests.Proto;
using BarkFluff.GrpcServer.Tracker;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BarkFluff.GrpcServer.Tests;

public class RequestContextProxyIdTests
{
    public static IEnumerable<object?[]> ProxyIds =>
    [
        ["lv-1", "lv-1"],
        [null, null],
        ["lv/1", null],
        [new string('a', 65), null],
    ];

    [Theory]
    [MemberData(nameof(ProxyIds))]
    public async Task ProxyId_IsValidatedAndAddedToLogContext(string? incoming, string? expected)
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext()
            .WriteTo.Sink(sink).CreateLogger();
        using var host = await StartHost(logger);
        var address = host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var channel = GrpcChannel.ForAddress(address);
        var headers = new Metadata();
        if (incoming is not null)
            headers.Add("x-bft-proxy-id", incoming);

        await new TraceProbeApi.TraceProbeApiClient(channel)
            .UnaryAsync(new ProbeRequest(), headers);

        var logEvent = sink.Events.Last(value => value.MessageTemplate.Text.StartsWith("proxy id probe", StringComparison.Ordinal));
        Assert.Equal(expected, ((ScalarValue)logEvent.Properties["ContextProxyId"]).Value);
        if (expected is null)
            Assert.False(logEvent.Properties.ContainsKey("ProxyId"));
        else
            Assert.Equal(expected, ((ScalarValue)logEvent.Properties["ProxyId"]).Value);
    }

    [Fact]
    public async Task ProxyId_WithRepeatedHeaderValues_IsRejected()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext()
            .WriteTo.Sink(sink).CreateLogger();
        using var host = await StartHost(logger);
        var address = host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var channel = GrpcChannel.ForAddress(address);
        var headers = new Metadata
        {
            { "x-bft-proxy-id", "lv-1" },
            { "x-bft-proxy-id", "lv-2" },
        };

        await new TraceProbeApi.TraceProbeApiClient(channel)
            .UnaryAsync(new ProbeRequest(), headers);

        var logEvent = sink.Events.Last(value => value.MessageTemplate.Text.StartsWith("proxy id probe", StringComparison.Ordinal));
        Assert.Null(((ScalarValue)logEvent.Properties["ContextProxyId"]).Value);
        Assert.False(logEvent.Properties.ContainsKey("ProxyId"));
    }

    [Fact]
    public async Task ProxyId_IsNotAddedToStreamingRequestContexts()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext()
            .WriteTo.Sink(sink).CreateLogger();
        using var host = await StartHost(logger);
        var address = host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var channel = GrpcChannel.ForAddress(address);
        var client = new TraceProbeApi.TraceProbeApiClient(channel);
        var headers = new Metadata { { "x-bft-proxy-id", "lv-1" } };

        using (var call = client.ServerStream(new ProbeRequest(), headers))
        {
            Assert.True(await call.ResponseStream.MoveNext());
            Assert.False(await call.ResponseStream.MoveNext());
        }

        using (var call = client.ClientStream(headers))
        {
            await call.RequestStream.WriteAsync(new ProbeRequest());
            await call.RequestStream.CompleteAsync();
            await call.ResponseAsync;
        }

        using (var call = client.Duplex(headers))
        {
            await call.RequestStream.WriteAsync(new ProbeRequest());
            await call.RequestStream.CompleteAsync();
            Assert.True(await call.ResponseStream.MoveNext());
            Assert.False(await call.ResponseStream.MoveNext());
        }

        var events = sink.Events
            .Where(value => value.MessageTemplate.Text.StartsWith("proxy id probe", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, events.Length);
        foreach (var logEvent in events)
        {
            Assert.Null(((ScalarValue)logEvent.Properties["ContextProxyId"]).Value);
            Assert.False((bool)((ScalarValue)logEvent.Properties["ContextInitialized"]).Value!);
            Assert.False(logEvent.Properties.ContainsKey("ProxyId"));
        }
    }

    private static async Task<IHost> StartHost(Serilog.ILogger logger)
    {
        var host = new HostBuilder().ConfigureWebHost(web => web.UseKestrel(options =>
                options.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2))
            .ConfigureServices(services =>
            {
                services.AddSingleton(logger);
                services.AddLogging(options => options.ClearProviders().AddSerilog(logger, dispose: false));
                services.AddBarkFluffGrpc();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGrpcService<ProxyIdProbeService>());
            })).Build();
        await host.StartAsync();
        return host;
    }

    public sealed class ProxyIdProbeService(IRequestContextAccessor requestContextAccessor, ILogger<ProxyIdProbeService> logger)
        : TraceProbeApi.TraceProbeApiBase
    {
        public override Task<ProbeReply> Unary(ProbeRequest request, ServerCallContext context)
            => Task.FromResult(Reply());

        public override async Task ServerStream(
            ProbeRequest request,
            IServerStreamWriter<ProbeReply> stream,
            ServerCallContext context)
        {
            await stream.WriteAsync(Reply());
        }

        public override async Task<ProbeReply> ClientStream(
            IAsyncStreamReader<ProbeRequest> stream,
            ServerCallContext context)
        {
            await foreach (var _ in stream.ReadAllAsync()) { }
            return Reply();
        }

        public override async Task Duplex(
            IAsyncStreamReader<ProbeRequest> stream,
            IServerStreamWriter<ProbeReply> output,
            ServerCallContext context)
        {
            await foreach (var _ in stream.ReadAllAsync())
                await output.WriteAsync(Reply());
        }

        private ProbeReply Reply()
        {
            RequestContext? requestContext = null;
            try
            {
                requestContext = requestContextAccessor.Current;
            }
            catch (InvalidOperationException)
            {
            }

            logger.LogInformation(
                "proxy id probe with context {ContextProxyId} (initialized={ContextInitialized})",
                requestContext?.ProxyId,
                requestContext is not null);
            return new ProbeReply();
        }
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
