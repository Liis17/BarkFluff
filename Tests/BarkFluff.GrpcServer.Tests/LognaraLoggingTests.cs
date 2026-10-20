using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

using BarkFluff.GrpcServer;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using Serilog;
using Serilog.Context;

namespace BarkFluff.GrpcServer.Tests;

[Collection("Process logging")]
public sealed class LognaraLoggingTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Enabled_sinks_are_independent(bool lognaraEnable, bool seqEnable, bool consoleOutput)
    {
        var lognaraBodies = new ConcurrentQueue<string>();
        var seqBodies = new ConcurrentQueue<string>();
        var requestPaths = new ConcurrentQueue<string>();
        var lognaraContentTypes = new ConcurrentQueue<string?>();
        var invalidLognaraRequests = new ConcurrentQueue<string>();
        var lognaraReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seqReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiverBuilder = WebApplication.CreateBuilder();
        receiverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var receiver = receiverBuilder.Build();
        receiver.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method))
            {
                await next(context);
                return;
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            requestPaths.Enqueue(context.Request.Path + context.Request.QueryString);
            if (context.Request.Path == "/v1/logs")
            {
                lognaraContentTypes.Enqueue(context.Request.ContentType);
                lognaraBodies.Enqueue(body);
                try
                {
                    if (!string.Equals(context.Request.ContentType?.Split(';')[0], "application/json", StringComparison.OrdinalIgnoreCase))
                        throw new JsonException("Lognara expects application/json.");

                    using var json = JsonDocument.Parse(body);
                    if (json.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                        throw new JsonException("Lognara expects one JSON object or array.");
                }
                catch (JsonException exception)
                {
                    invalidLognaraRequests.Enqueue($"{context.Request.ContentType}: {exception.Message}: {body}");
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }
            else if (context.Request.Path == "/api/events/raw")
                seqBodies.Enqueue(body);
            if (context.Request.Path == "/v1/logs" && body.Contains("ServiceMetrics", StringComparison.Ordinal))
                lognaraReceived.TrySetResult();
            if (context.Request.Path == "/api/events/raw")
                seqReceived.TrySetResult();
            context.Response.StatusCode = context.Request.Path == "/v1/logs"
                ? StatusCodes.Status202Accepted
                : StatusCodes.Status201Created;
        });
        await receiver.StartAsync();

        var directory = Path.Combine(Path.GetTempPath(), "barkfluff-logging-tests", Guid.NewGuid().ToString("N"));
        var originalConsole = Console.Out;
        using var console = new StringWriter();
        using var selfLog = new StringWriter();
        Serilog.Debugging.SelfLog.Enable(selfLog.WriteLine);
        Console.SetOut(console);
        try
        {
            var config = Configuration(new Dictionary<string, string?>
            {
                ["LognaraEnable"] = lognaraEnable.ToString(),
                ["SeqEnable"] = seqEnable.ToString(),
                ["ConsoleOutput"] = consoleOutput.ToString(),
                ["Lognara:AgentUrl"] = receiver.Urls.Single() + "/v1/logs",
                ["Seq:ServerUrl"] = receiver.Urls.Single(),
            });
            var options = BarkFluffLoggingOptions.Resolve(config, "BarkFluff.Tests");

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.With<ActivityLogEnricher>();
            SerilogExtensions.WriteToBarkFluffSinks(loggerConfiguration, options, directory);
            var logger = loggerConfiguration.CreateLogger();
            try
            {
                string traceId;
                string spanId;
                string parentSpanId;
                using (var parent = new Activity("parent").SetIdFormat(ActivityIdFormat.W3C).Start())
                {
                    using var activity = new Activity("delivery")
                        .SetIdFormat(ActivityIdFormat.W3C)
                        .SetParentId(parent.Id!)
                        .Start();
                    traceId = activity.TraceId.ToHexString();
                    spanId = activity.SpanId.ToHexString();
                    parentSpanId = parent.SpanId.ToHexString();

                    using (LogContext.PushProperty("RequestId", "request-42"))
                    using (LogContext.PushProperty("CorrelationId", "business-42"))
                    {
                        logger.Information("Delivery test");
                        logger.Error("Error without exception");
                        logger.Error(new InvalidOperationException("failure"), "Error with exception");
                        var metrics = new { SchemaVersion = 2, Counters = new { messages_sent = 3 }, Gauges = new { active = 4 } };
                        logger.Information("ServiceMetrics without exception {@Metrics}", metrics);
                        logger.Error(new InvalidOperationException("failure"), "ServiceMetrics with exception {@Metrics}", metrics);
                    }
                }

                logger.Dispose();

                if (lognaraEnable)
                {
                    try
                    {
                        await lognaraReceived.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    }
                    catch (TimeoutException exception)
                    {
                        throw new TimeoutException(
                            $"No Lognara metrics event arrived. Captured paths: {string.Join(",", requestPaths)}; bodies: {string.Join("\n", lognaraBodies)}; console: {console}; selflog: {selfLog}; files: {string.Join("\n", Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(path => path + "\n" + File.ReadAllText(path)) : [])}",
                            exception);
                    }
                }
                if (seqEnable)
                    await seqReceived.Task.WaitAsync(TimeSpan.FromSeconds(15));

                Assert.Empty(invalidLognaraRequests);
                if (lognaraEnable)
                {
                    AssertLognaraEvent(lognaraBodies, traceId, spanId, parentSpanId);
                    Assert.All(lognaraContentTypes, contentType =>
                        Assert.Equal("application/json", contentType?.Split(';')[0], ignoreCase: true));
                }
                else
                {
                    Assert.Empty(lognaraBodies);
                }

                if (seqEnable)
                {
                    Assert.NotEmpty(seqBodies);
                    Assert.Contains("Delivery test", string.Join('\n', seqBodies));
                }
                else
                {
                    Assert.Empty(seqBodies);
                }

                if (consoleOutput)
                    Assert.Contains("Delivery test", console.ToString());
                else
                    Assert.Empty(console.ToString());

                var files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];
                Assert.Equal(seqEnable, files.Any(path => Path.GetFileName(path).StartsWith("seq-buffer", StringComparison.Ordinal)));
                Assert.Equal(lognaraEnable, files.Any(path => Path.GetFileName(path).StartsWith("lognara-buffer", StringComparison.Ordinal)));
            }
            finally
            {
                logger.Dispose();
            }
        }
        finally
        {
            Console.SetOut(originalConsole);
            Serilog.Debugging.SelfLog.Disable();
            await receiver.StopAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Logging_source_priority_is_command_line_then_environment_then_settings()
    {
        var config = Configuration(new Dictionary<string, string?>
        {
            ["LognaraEnable"] = "true",
            ["SeqEnable"] = "true",
            ["ConsoleOutput"] = "false",
        });

        var options = BarkFluffLoggingOptions.Resolve(
            config,
            "BarkFluff.Users",
            commandLineValue: key => key == "LognaraEnable" ? "false" : null,
            environmentValue: key => key == "SeqEnable" ? "false" : null);

        Assert.False(options.LognaraEnable);
        Assert.False(options.SeqEnable);
        Assert.False(options.ConsoleOutput);
    }

    [Fact]
    public void Missing_flags_default_to_lognara_off_seq_and_console_on()
    {
        var options = BarkFluffLoggingOptions.Resolve(
            Configuration(new Dictionary<string, string?>()),
            "BarkFluff.Tests",
            commandLineValue: _ => null,
            environmentValue: _ => null);

        Assert.False(options.LognaraEnable);
        Assert.True(options.SeqEnable);
        Assert.True(options.ConsoleOutput);
        Assert.Null(options.LognaraAgentUri);
        Assert.Equal("http://seq:5341/", options.SeqServerUri!.AbsoluteUri);
    }

    [Fact]
    public void Pre_remote_console_flag_uses_command_line_then_environment_then_local_configuration()
    {
        var config = Configuration(new Dictionary<string, string?> { ["ConsoleOutput"] = "true" });

        Assert.False(BarkFluffLoggingOptions.ResolveConsoleOutput(
            config,
            commandLineValue: _ => null,
            environmentValue: key => key == "ConsoleOutput" ? "false" : null));
        Assert.False(BarkFluffLoggingOptions.ResolveConsoleOutput(
            config,
            commandLineValue: key => key == "ConsoleOutput" ? "false" : null,
            environmentValue: _ => "true"));
        Assert.True(BarkFluffLoggingOptions.ResolveConsoleOutput(
            Configuration(new Dictionary<string, string?>()),
            commandLineValue: _ => null,
            environmentValue: _ => null));
    }

    [Fact]
    public void Command_line_remains_higher_priority_than_a_later_settings_provider()
    {
        var config = new ConfigurationBuilder()
            .AddCommandLine(["--LognaraEnable=false"])
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LognaraEnable"] = "true" })
            .Build();

        var options = BarkFluffLoggingOptions.Resolve(config, "BarkFluff.Users", environmentValue: _ => null);

        Assert.False(options.LognaraEnable);
    }

    [Theory]
    [InlineData("LognaraEnable")]
    [InlineData("SeqEnable")]
    [InlineData("ConsoleOutput")]
    public void Invalid_boolean_names_the_option(string key)
    {
        var config = Configuration(new Dictionary<string, string?> { [key] = "enabled" });

        var exception = Assert.Throws<InvalidOperationException>(() => BarkFluffLoggingOptions.Resolve(config, "BarkFluff.Tests"));

        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public void Disabled_sinks_do_not_validate_their_urls()
    {
        var config = Configuration(new Dictionary<string, string?>
        {
            ["LognaraEnable"] = "false",
            ["SeqEnable"] = "false",
            ["Lognara:AgentUrl"] = "not-a-url",
            ["Seq:ServerUrl"] = "also-not-a-url",
        });

        var options = BarkFluffLoggingOptions.Resolve(config, "BarkFluff.Tests");

        Assert.Null(options.LognaraAgentUri);
        Assert.Null(options.SeqServerUri);
    }

    [Fact]
    public void Embedded_agent_requires_the_fixed_local_endpoint_only_when_enabled()
    {
        var disabled = BarkFluffLoggingOptions.Resolve(
            Configuration(new Dictionary<string, string?>
            {
                ["LognaraEnable"] = "false",
                ["Lognara:AgentUrl"] = "invalid",
                ["Lognara:AgentExecutable"] = "missing-agent",
            }),
            "BarkFluff.Tests",
            commandLineValue: _ => null,
            environmentValue: _ => null);
        Assert.Null(disabled.LognaraAgentUri);
        Assert.Null(disabled.AgentExecutable);

        var exception = Assert.Throws<InvalidOperationException>(() => BarkFluffLoggingOptions.Resolve(
            Configuration(new Dictionary<string, string?>
            {
                ["LognaraEnable"] = "true",
                ["Lognara:AgentUrl"] = "http://remote-agent:7400/v1/logs",
                ["Lognara:AgentExecutable"] = "missing-agent",
            }),
            "BarkFluff.Tests",
            commandLineValue: _ => null,
            environmentValue: _ => null));
        Assert.Contains("Lognara:AgentUrl", exception.Message);
    }

    [Theory]
    [InlineData("LognaraEnable", "true", "Lognara:AgentUrl", "not-a-url", "Lognara:AgentUrl")]
    [InlineData("SeqEnable", "true", "Seq:ServerUrl", "not-a-url", "Seq:ServerUrl")]
    public void Enabled_sink_validates_its_url(string flag, string enabled, string urlKey, string url, string expectedErrorKey)
    {
        var config = Configuration(new Dictionary<string, string?> { [flag] = enabled, [urlKey] = url });

        var exception = Assert.Throws<InvalidOperationException>(() => BarkFluffLoggingOptions.Resolve(config, "BarkFluff.Tests"));

        Assert.Contains(expectedErrorKey, exception.Message);
    }

    private static void AssertLognaraEvent(
        ConcurrentQueue<string> bodies,
        string traceId,
        string spanId,
        string parentSpanId)
    {
        Assert.NotEmpty(bodies);
        var events = new List<JsonElement>();
        foreach (var body in bodies)
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
            {
                events.AddRange(json.RootElement.EnumerateArray().Select(item => item.Clone()));
            }
            else
            {
                Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
                events.Add(json.RootElement.Clone());
            }
        }

        var deliveryEvent = events.FirstOrDefault(item => item.GetProperty("message").GetString() == "Delivery test");
        var metricsEvent = events.FirstOrDefault(item => item.GetProperty("message").GetString()!.StartsWith("ServiceMetrics with exception", StringComparison.Ordinal));
        var metricsWithoutException = events.FirstOrDefault(item => item.GetProperty("message").GetString()!.StartsWith("ServiceMetrics without exception", StringComparison.Ordinal));
        var errorWithoutException = events.FirstOrDefault(item => item.GetProperty("message").GetString() == "Error without exception");
        var errorWithException = events.FirstOrDefault(item => item.GetProperty("message").GetString() == "Error with exception");
        Assert.True(deliveryEvent.ValueKind != JsonValueKind.Undefined &&
                    metricsEvent.ValueKind != JsonValueKind.Undefined &&
                    metricsWithoutException.ValueKind != JsonValueKind.Undefined &&
                    errorWithoutException.ValueKind != JsonValueKind.Undefined &&
                    errorWithException.ValueKind != JsonValueKind.Undefined,
            string.Join(Environment.NewLine, events.Select(item => item.ToString())));

        Assert.Equal(traceId, deliveryEvent.GetProperty("trace_id").GetString());
        Assert.Equal(spanId, deliveryEvent.GetProperty("span_id").GetString());
        Assert.Equal(parentSpanId, deliveryEvent.GetProperty("attributes").GetProperty("ParentSpanId").GetString());
        Assert.Equal("request-42", deliveryEvent.GetProperty("attributes").GetProperty("RequestId").GetString());
        Assert.Equal("business-42", deliveryEvent.GetProperty("attributes").GetProperty("CorrelationId").GetString());
        Assert.Equal(2, metricsEvent.GetProperty("attributes").GetProperty("Metrics").GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(3, metricsEvent.GetProperty("attributes").GetProperty("Metrics").GetProperty("Counters").GetProperty("messages_sent").GetInt32());
        Assert.Equal(4, metricsEvent.GetProperty("attributes").GetProperty("Metrics").GetProperty("Gauges").GetProperty("active").GetInt32());
        Assert.Equal(2, metricsWithoutException.GetProperty("attributes").GetProperty("Metrics").GetProperty("SchemaVersion").GetInt32());
        Assert.False(errorWithoutException.TryGetProperty("exception", out _));
        Assert.Contains(typeof(InvalidOperationException).FullName!, errorWithException.GetProperty("exception").GetString());
        Assert.Contains("failure", metricsEvent.GetProperty("exception").GetString());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
