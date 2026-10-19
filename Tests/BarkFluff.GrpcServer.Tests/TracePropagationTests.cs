using System.Diagnostics;

using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Tracing;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BarkFluff.GrpcServer.Tests;

public class TracePropagationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestScope_CoversNormalAndFailedRequests(bool fail)
    {
        const string traceId = "123456789012345678901234567890ab";
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext()
            .Enrich.With<ActivityLogEnricher>().WriteTo.Sink(sink).CreateLogger();
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services => services.AddBarkFluffTracing())
            .Configure(app => app.Run(async context =>
            {
                try
                {
                    await Task.Yield();
                    if (fail)
                        throw new InvalidOperationException("request failure");
                    logger.Information("handled");
                }
                catch (Exception exception)
                {
                    logger.Error(exception, "failed");
                    context.Response.StatusCode = 500;
                }
            }))).StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("traceparent", $"00-{traceId}-1234567890123456-00");
        request.Headers.Add("X-Correlation-ID", "business-operation");
        using var response = await host.GetTestClient().SendAsync(request);
        Assert.Equal(fail ? 500 : 200, (int)response.StatusCode);
        Assert.Equal(traceId, Scalar(sink.Event!, "TraceId"));
        Assert.Equal("business-operation", Scalar(sink.Event!, "CorrelationId"));
        Assert.False(string.IsNullOrWhiteSpace(Scalar(sink.Event!, "RequestId")));
        Assert.Null(TraceContext.CorrelationId);
    }

    [Fact]
    public async Task HttpClient_PropagatesSameTraceToNextService()
    {
        using var diagnostics = new TraceDiagnostics();
        await diagnostics.StartAsync(CancellationToken.None);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddBarkFluffTracing();
        await using var app = builder.Build();
        app.MapGet("/", () =>
        {
            received.TrySetResult(Activity.Current!.Id!);
            return "ok";
        });
        await app.StartAsync();
        using var activity = new Activity("origin").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var client = new HttpClient();
        using var response = await client.GetAsync(app.Urls.Single());
        Assert.True(ActivityContext.TryParse(await received.Task.WaitAsync(TimeSpan.FromSeconds(10)), null, out var remote));
        Assert.Equal(activity.TraceId, remote.TraceId);
        Assert.NotEqual(activity.SpanId, remote.SpanId);
        await app.StopAsync();
    }

    [Fact]
    public async Task MassTransit_PublishConsume_PreservesTraceAfterOriginEnds()
    {
        using var diagnostics = new TraceDiagnostics();
        await diagnostics.StartAsync(CancellationToken.None);
        var received = new TaskCompletionSource<ActivityContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<TraceProbeConsumer>();
            bus.UsingInMemory((context, config) => config.ConfigureEndpoints(context));
        });
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            ActivityContext origin;
            using (var activity = new Activity("origin").SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                origin = activity.Context;
                await bus.Publish(new TraceProbe(Guid.NewGuid()));
            }
            var consumed = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(origin.TraceId, consumed.TraceId);
            Assert.NotEqual(origin.SpanId, consumed.SpanId);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-parent")]
    [InlineData("00-123456789012345678901234567890ab-1234567890123456-00")]
    public void Dispatch_RestoresParentAndDoesNotLeakBetweenOperations(string? parent)
    {
        using var unrelated = new Activity("unrelated").SetIdFormat(ActivityIdFormat.W3C).Start();
        using (TraceContext.PushCorrelationId("outer"))
        {
            using (TraceContext.StartDispatch("dispatch", parent, "vendor=value", "business"))
            {
                var current = Activity.Current!;
                Assert.NotEqual(unrelated.TraceId, current.TraceId);
                Assert.Equal("business", TraceContext.CorrelationId);
                if (parent is not null && ActivityContext.TryParse(parent, "vendor=value", out var expected))
                {
                    Assert.Equal(expected.TraceId, current.TraceId);
                    Assert.Equal(expected.SpanId, current.ParentSpanId);
                    Assert.Equal("vendor=value", current.TraceStateString);
                }
            }
            Assert.Same(unrelated, Activity.Current);
            Assert.Equal("outer", TraceContext.CorrelationId);
        }
    }

    private static string? Scalar(LogEvent evt, string name) =>
        evt.Properties.TryGetValue(name, out var value) ? (value as ScalarValue)?.Value?.ToString() : null;

    private sealed class CaptureSink : ILogEventSink
    {
        public LogEvent? Event { get; private set; }
        public void Emit(LogEvent logEvent) => Event = logEvent;
    }
}

public record TraceProbe(Guid Id);

public class TraceProbeConsumer(TaskCompletionSource<ActivityContext> received) : IConsumer<TraceProbe>
{
    public Task Consume(ConsumeContext<TraceProbe> context)
    {
        received.TrySetResult(Activity.Current?.Context ?? default);
        return Task.CompletedTask;
    }
}
