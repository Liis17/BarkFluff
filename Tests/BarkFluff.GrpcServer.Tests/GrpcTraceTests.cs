using System.Collections.Concurrent;
using System.Diagnostics;

using BarkFluff.GrpcServer.Tests.Proto;

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

public class GrpcTraceTests
{
    [Theory]
    [InlineData("unary")]
    [InlineData("server")]
    [InlineData("client")]
    [InlineData("duplex")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task EveryRpcShape_LogsInsideRequestTraceScope(string shape)
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext()
            .Enrich.With<ActivityLogEnricher>().WriteTo.Sink(sink).CreateLogger();
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseKestrel(options =>
                options.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2))
            .ConfigureServices(services =>
            {
                services.AddBarkFluffTracing();
                services.AddSingleton<Serilog.ILogger>(logger);
                services.AddLogging(options => options.ClearProviders().AddSerilog(logger, dispose: false));
                services.AddGrpc(options => options.Interceptors.Add<ServerExceptionInterceptor>());
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGrpcService<ProbeService>());
            })).StartAsync();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var channel = GrpcChannel.ForAddress(address);
        var client = new TraceProbeApi.TraceProbeApiClient(channel);
        using var origin = new Activity("client").SetIdFormat(ActivityIdFormat.W3C).Start();
        var headers = new Metadata { { "x-correlation-id", "business" } };
        switch (shape)
        {
            case "unary":
                await client.UnaryAsync(new ProbeRequest(), headers);
                break;
            case "failure":
                await Assert.ThrowsAsync<RpcException>(async () =>
                    await client.UnaryAsync(new ProbeRequest { Fail = true }, headers));
                break;
            case "server":
            case "cancellation":
                using (var cancellation = new CancellationTokenSource())
                using (var call = client.ServerStream(new ProbeRequest
                       { WaitForCancellation = shape == "cancellation" }, headers, cancellationToken: cancellation.Token))
                {
                    Assert.True(await call.ResponseStream.MoveNext());
                    if (shape == "cancellation")
                    {
                        cancellation.Cancel();
                        await sink.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    else
                        Assert.False(await call.ResponseStream.MoveNext());
                }
                break;
            case "client":
                using (var call = client.ClientStream(headers))
                {
                    await call.RequestStream.WriteAsync(new ProbeRequest());
                    await call.RequestStream.CompleteAsync();
                    await call.ResponseAsync;
                }
                break;
            case "duplex":
                using (var call = client.Duplex(headers))
                {
                    await call.RequestStream.WriteAsync(new ProbeRequest());
                    await call.RequestStream.CompleteAsync();
                    Assert.True(await call.ResponseStream.MoveNext());
                    Assert.False(await call.ResponseStream.MoveNext());
                }
                break;
        }

        var evt = sink.Events.Last(value => shape == "failure"
            ? value.MessageTemplate.Text.StartsWith("КРИТИЧЕСКАЯ ОШИБКА")
            : value.MessageTemplate.Text.StartsWith("probe"));
        Assert.Equal(origin.TraceId.ToHexString(), ((ScalarValue)evt.Properties["TraceId"]).Value);
        Assert.Equal("business", ((ScalarValue)evt.Properties["CorrelationId"]).Value);
        Assert.False(string.IsNullOrWhiteSpace(((ScalarValue)evt.Properties["RequestId"]).Value?.ToString()));
    }

    public class ProbeService(Serilog.ILogger logger) : TraceProbeApi.TraceProbeApiBase
    {
        public override Task<ProbeReply> Unary(ProbeRequest request, ServerCallContext context)
        {
            if (request.Fail)
                throw new InvalidOperationException("test failure");
            return Task.FromResult(Reply());
        }

        public override async Task ServerStream(ProbeRequest request, IServerStreamWriter<ProbeReply> stream, ServerCallContext context)
        {
            await stream.WriteAsync(Reply());
            if (request.WaitForCancellation)
            {
                try { await Task.Delay(Timeout.Infinite, context.CancellationToken); }
                catch (OperationCanceledException) { logger.Information("probe cancelled"); }
            }
        }

        public override async Task<ProbeReply> ClientStream(IAsyncStreamReader<ProbeRequest> stream, ServerCallContext context)
        {
            await foreach (var _ in stream.ReadAllAsync()) { }
            return Reply();
        }

        public override async Task Duplex(IAsyncStreamReader<ProbeRequest> stream, IServerStreamWriter<ProbeReply> output, ServerCallContext context)
        {
            await foreach (var _ in stream.ReadAllAsync())
                await output.WriteAsync(Reply());
        }

        private ProbeReply Reply()
        {
            logger.Information("probe handled");
            return new ProbeReply { TraceId = Activity.Current!.TraceId.ToHexString() };
        }
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Emit(LogEvent logEvent)
        {
            Events.Enqueue(logEvent);
            if (logEvent.MessageTemplate.Text == "probe cancelled")
                Cancelled.TrySetResult();
        }
    }
}
