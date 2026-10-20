using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

using BarkFluff.GrpcServer;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Serilog;

namespace BarkFluff.GrpcServer.Tests;

public sealed class LognaraAgentLifecycleTests
{
    private static readonly Uri AgentUri = new("http://127.0.0.1:7400/v1/logs");

    [Fact]
    public async Task Disabled_agent_does_not_start_and_cleanup_is_idempotent()
    {
        var service = CreateService(Options(lognaraEnable: false, executable: "missing-lognara-agent"), new TestLifetime());

        await service.StartingAsync(CancellationToken.None);
        await service.StoppingAsync(CancellationToken.None);
        await service.StoppedAsync(CancellationToken.None);
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task External_agent_does_not_require_relay_token_or_start_a_process()
    {
        using var environment = new EnvironmentOverride(("LOGNARA_RELAY_TOKEN", null));
        var service = CreateService(Options(lognaraEnable: true, executable: null), new TestLifetime());

        await service.StartingAsync(CancellationToken.None);
        await service.StoppingAsync(CancellationToken.None);
        await service.StoppedAsync(CancellationToken.None);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Embedded_agent_requires_relay_token_only_when_enabled()
    {
        using var environment = new EnvironmentOverride(("LOGNARA_RELAY_TOKEN", null));
        var service = CreateService(Options(lognaraEnable: true, executable: "missing-lognara-agent"), new TestLifetime());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartingAsync(CancellationToken.None));

        Assert.Contains("LOGNARA_RELAY_TOKEN", exception.Message);
        Assert.DoesNotContain("test-token", exception.ToString());
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Embedded_agent_validates_relay_url_before_process_start()
    {
        using var environment = new EnvironmentOverride(
            ("LOGNARA_RELAY_TOKEN", "test-token"),
            ("LOGNARA_RELAY_URL", "ftp://relay.invalid/v1/batches"));
        var service = CreateService(Options(lognaraEnable: true, executable: "missing-lognara-agent"), new TestLifetime());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartingAsync(CancellationToken.None));

        Assert.Contains("LOGNARA_RELAY_URL", exception.Message);
        Assert.DoesNotContain("test-token", exception.ToString());
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Embedded_agent_start_failure_disposes_unstarted_process()
    {
        using var environment = new EnvironmentOverride(
            ("LOGNARA_RELAY_TOKEN", "test-token"),
            ("LOGNARA_RELAY_URL", null));
        var executable = Path.Combine(Path.GetTempPath(), "missing-lognara-agent-" + Guid.NewGuid().ToString("N"));
        var service = CreateService(Options(lognaraEnable: true, executable), new TestLifetime());

        await Assert.ThrowsAnyAsync<Win32Exception>(() => service.StartingAsync(CancellationToken.None));
        await service.DisposeAsync();
    }

    [LinuxProcessFact]
    public async Task Embedded_agent_is_ready_before_start_returns_and_shutdown_flushes_then_signals()
    {
        EnsureAgentPortAvailable();
        var directory = CreateTempDirectory();
        var capturePath = Path.Combine(directory, "agent-capture.txt");
        var script = CreateAgentScript(directory);
        using var environment = AgentEnvironment(capturePath, crashAfterReady: false);
        var lifetime = new TestLifetime();
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var capturedOutput = new SignalingTextWriter();
        using var capturedError = new SignalingTextWriter();
        Console.SetOut(capturedOutput);
        Console.SetError(capturedError);
        var originalLogger = Log.Logger;
        var logDirectory = Path.Combine(directory, "logs");
        var service = CreateService(Options(lognaraEnable: true, executable: script, consoleOutput: true), lifetime);

        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Lognara(AgentUri.AbsoluteUri, bufferBaseFileName: Path.Combine(logDirectory, "lognara-buffer"))
                .CreateLogger();

            await service.StartingAsync(CancellationToken.None);
            Assert.Equal("agent stdout marker", await capturedOutput.NextLine.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("agent stderr marker", await capturedError.NextLine.WaitAsync(TimeSpan.FromSeconds(5)));

            Log.Information("Lifecycle flush marker");
            await service.StoppingAsync(CancellationToken.None);
            await service.StoppedAsync(CancellationToken.None);
            await service.DisposeAsync();

            var capture = await File.ReadAllTextAsync(capturePath);
            Assert.Contains("ENV OK", capture);
            Assert.Contains("GET /v1/logs", capture);
            Assert.Contains("POST /v1/logs CT=application/json; charset=utf-8", capture);
            Assert.Contains("Lifecycle flush marker", capture);
            Assert.Contains("TERM", capture);
            Assert.DoesNotContain("test-token", capture);
        }
        finally
        {
            try
            {
                await service.DisposeAsync();
                await Log.CloseAndFlushAsync();
            }
            finally
            {
                Log.Logger = originalLogger;
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    [LinuxProcessFact]
    public async Task Console_output_false_discards_agent_stdout_and_stderr()
    {
        EnsureAgentPortAvailable();
        var directory = CreateTempDirectory();
        var capturePath = Path.Combine(directory, "agent-capture.txt");
        var script = CreateAgentScript(directory);
        using var environment = AgentEnvironment(capturePath, crashAfterReady: false);
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var capturedOutput = new StringWriter();
        using var capturedError = new StringWriter();
        Console.SetOut(capturedOutput);
        Console.SetError(capturedError);
        var originalLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().CreateLogger();
        var service = CreateService(Options(lognaraEnable: true, executable: script, consoleOutput: false), new TestLifetime());

        try
        {
            await service.StartingAsync(CancellationToken.None);
            await service.StoppingAsync(CancellationToken.None);
            await service.StoppedAsync(CancellationToken.None);

            Assert.Empty(capturedOutput.ToString());
            Assert.Empty(capturedError.ToString());
            Assert.Contains("GET /v1/logs", await File.ReadAllTextAsync(capturePath));
        }
        finally
        {
            try
            {
                await service.DisposeAsync();
                await Log.CloseAndFlushAsync();
            }
            finally
            {
                Log.Logger = originalLogger;
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    [LinuxProcessFact]
    public async Task Agent_exit_before_readiness_fails_startup_and_cleans_up_process()
    {
        EnsureAgentPortAvailable();
        var directory = CreateTempDirectory();
        var capturePath = Path.Combine(directory, "agent-capture.txt");
        var script = CreateAgentScript(directory);
        using var environment = AgentEnvironment(capturePath, crashAfterReady: false, failBeforeReady: true);
        var originalLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().CreateLogger();
        var service = CreateService(Options(lognaraEnable: true, executable: script), new TestLifetime());

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.StartingAsync(CancellationToken.None));

            Assert.Contains("exited before becoming ready", exception.Message);
            Assert.Contains("STARTUP FAILURE", await File.ReadAllTextAsync(capturePath));
        }
        finally
        {
            try
            {
                await service.DisposeAsync();
                await Log.CloseAndFlushAsync();
            }
            finally
            {
                Log.Logger = originalLogger;
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    [LinuxProcessFact]
    public async Task Unexpected_agent_exit_stops_application_with_nonzero_exit_code()
    {
        EnsureAgentPortAvailable();
        var directory = CreateTempDirectory();
        var capturePath = Path.Combine(directory, "agent-capture.txt");
        var script = CreateAgentScript(directory);
        using var environment = AgentEnvironment(capturePath, crashAfterReady: true);
        var lifetime = new TestLifetime();
        var originalExitCode = Environment.ExitCode;
        var originalLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().CreateLogger();
        var service = CreateService(Options(lognaraEnable: true, executable: script, consoleOutput: false), lifetime);

        try
        {
            await service.StartingAsync(CancellationToken.None);
            await lifetime.Stopping.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, Environment.ExitCode);
            await service.StoppedAsync(CancellationToken.None);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
            try
            {
                await service.DisposeAsync();
                await Log.CloseAndFlushAsync();
            }
            finally
            {
                Log.Logger = originalLogger;
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static LognaraAgentLifecycleService CreateService(BarkFluffLoggingOptions options, TestLifetime lifetime) =>
        new(options, lifetime, NullLogger<LognaraAgentLifecycleService>.Instance);

    private static BarkFluffLoggingOptions Options(bool lognaraEnable, string? executable, bool consoleOutput = false) =>
        new(lognaraEnable, SeqEnable: false, consoleOutput, lognaraEnable ? AgentUri : null, null, executable, "BarkFluff.Tests");

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "barkfluff-lognara-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateAgentScript(string directory)
    {
        var script = Path.Combine(directory, "fake-lognara-agent");
        File.WriteAllText(script, """
            #!/usr/bin/python3
            import http.server
            import json
            import os
            import signal
            import sys
            import threading

            capture = os.environ["LOGNARA_TEST_CAPTURE"]
            def record(value):
                with open(capture, "ab") as output:
                    output.write(value + b"\n")

            env_ok = (
                os.environ.get("LOGNARA_SERVICE") == "BarkFluff.Tests"
                and bool(os.environ.get("LOGNARA_SERVER"))
                and os.environ.get("LOGNARA_BACKEND") == "barkfluff"
                and os.environ.get("LOGNARA_LISTEN_ADDR") == "127.0.0.1:7400"
                and os.environ.get("LOGNARA_RELAY_URL") == "http://lognara-relay:7401/v1/batches"
                and bool(os.environ.get("LOGNARA_RELAY_TOKEN"))
                and len(sys.argv) == 1
            )
            record(b"ENV OK" if env_ok else b"ENV BAD")
            print("agent stdout marker", flush=True)
            print("agent stderr marker", file=sys.stderr, flush=True)
            if os.environ.get("LOGNARA_TEST_FAIL_BEFORE_READY") == "1":
                record(b"STARTUP FAILURE")
                os._exit(17)

            class Handler(http.server.BaseHTTPRequestHandler):
                def do_GET(self):
                    record(("GET " + self.path).encode())
                    self.send_response(405)
                    self.end_headers()

                def do_POST(self):
                    body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
                    content_type = self.headers.get("Content-Type", "")
                    try:
                        value = json.loads(body)
                        if not isinstance(value, (dict, list)):
                            raise ValueError("expected object or array")
                    except Exception as error:
                        record(("BAD JSON " + str(error)).encode())
                        self.send_response(400)
                        self.end_headers()
                        return
                    record(("POST " + self.path + " CT=" + content_type + " ").encode() + body)
                    self.send_response(202)
                    self.end_headers()

                def log_message(self, format, *args):
                    pass

            server = http.server.HTTPServer(("127.0.0.1", 7400), Handler)
            def terminate(signum, frame):
                record(b"TERM")
                os._exit(0)
            signal.signal(signal.SIGTERM, terminate)
            if os.environ.get("LOGNARA_TEST_CRASH") == "1":
                threading.Timer(1, lambda: os._exit(17)).start()
            server.serve_forever()
            """);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return script;
    }

    private static EnvironmentOverride AgentEnvironment(string capturePath, bool crashAfterReady, bool failBeforeReady = false) => new(
        ("LOGNARA_RELAY_TOKEN", "test-token"),
        ("LOGNARA_RELAY_URL", null),
        ("LOGNARA_SERVICE", null),
        ("LOGNARA_SERVER", null),
        ("LOGNARA_BACKEND", null),
        ("LOGNARA_LISTEN_ADDR", null),
        ("LOGNARA_TEST_CAPTURE", capturePath),
        ("LOGNARA_TEST_CRASH", crashAfterReady ? "1" : null),
        ("LOGNARA_TEST_FAIL_BEFORE_READY", failBeforeReady ? "1" : null));

    private static void EnsureAgentPortAvailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, AgentUri.Port);
        listener.Start();
    }

    private sealed class EnvironmentOverride : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public EnvironmentOverride(params (string Key, string? Value)[] values)
        {
            _originalValues = values.ToDictionary(value => value.Key, value => Environment.GetEnvironmentVariable(value.Key));
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _originalValues)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public Task Stopping => _stopping.Token.AsTask();

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class SignalingTextWriter : StringWriter
    {
        private readonly TaskCompletionSource<string> _nextLine = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> NextLine => _nextLine.Task;

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            _nextLine.TrySetResult(value ?? string.Empty);
        }
    }
}

public sealed class LinuxProcessFactAttribute : FactAttribute
{
    public LinuxProcessFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Requires Linux process and signal behavior; skipped on this platform.";
        else if (!File.Exists("/usr/bin/python3") && !File.Exists("/usr/local/bin/python3"))
            Skip = "Requires python3 for the isolated fake-agent process.";
        else
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, 7400);
                listener.Start();
            }
            catch (SocketException)
            {
                Skip = "Requires the local Lognara test endpoint 127.0.0.1:7400 to be free.";
            }
        }
    }
}

internal static class CancellationTokenTestExtensions
{
    public static Task AsTask(this CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetResult());
        return completion.Task;
    }
}
