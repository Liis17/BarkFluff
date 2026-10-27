using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace BarkFluff.GrpcServer;

internal sealed class LognaraAgentLifecycleService : IHostedLifecycleService, IAsyncDisposable
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly BarkFluffLoggingOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<LognaraAgentLifecycleService> _logger;
    private readonly object _shutdownSync = new();
    private Process? _process;
    private Task? _stdoutDrain;
    private Task? _stderrDrain;
    private Task? _watchTask;
    private Task? _shutdownTask;
    private int _stopping;
    private int _disposed;

    public LognaraAgentLifecycleService(
        BarkFluffLoggingOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<LognaraAgentLifecycleService> logger)
    {
        _options = options;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!_options.LognaraEnable || _options.AgentExecutable is null)
            return;

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.AgentExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ConfigureEnvironment(startInfo.Environment, _options.ServiceName);

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("The configured Lognara agent could not be started.");

            Volatile.Write(ref _process, process);
            _stdoutDrain = DrainOutputAsync(process.StandardOutput, standardError: false);
            _stderrDrain = DrainOutputAsync(process.StandardError, standardError: true);
            await WaitForReadinessAsync(process, _options.LognaraAgentUri!, cancellationToken);
            _watchTask = WatchForUnexpectedExitAsync(process);
            _logger.LogInformation("Embedded Lognara agent is ready.");
        }
        catch
        {
            if (process is not null && !ReferenceEquals(Volatile.Read(ref _process), process))
                process.Dispose();
            Interlocked.Exchange(ref _stopping, 1);
            await StopAndFlushAsync();
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => StopAndFlushAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Interlocked.Exchange(ref _stopping, 1);
        await StopAndFlushAsync();
    }

    private Task StopAndFlushAsync()
    {
        lock (_shutdownSync)
            return _shutdownTask ??= FlushAndStopAgentAsync();
    }

    private async Task FlushAndStopAgentAsync()
    {
        if (Volatile.Read(ref _process) is null)
            return;

        try
        {
            await Log.CloseAndFlushAsync().AsTask().WaitAsync(FlushTimeout);
        }
        catch (TimeoutException)
        {
            WriteShutdownDiagnostic("Serilog flush exceeded its shutdown timeout.");
        }
        catch (Exception exception)
        {
            WriteShutdownDiagnostic($"Serilog flush failed: {exception.GetType().Name}.");
        }

        await StopAgentAsync();
    }

    private async Task StopAgentAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return;

        try
        {
            if (!HasExited(process))
            {
                Interlocked.Exchange(ref _stopping, 1);
                SendTerminationSignal(process);

                try
                {
                    await process.WaitForExitAsync().WaitAsync(ShutdownTimeout);
                }
                catch (TimeoutException)
                {
                    if (!HasExited(process))
                        process.Kill();
                    await process.WaitForExitAsync().WaitAsync(DrainTimeout);
                }
            }

            var drains = new[] { _stdoutDrain, _stderrDrain }.Where(task => task is not null).Cast<Task>();
            try
            {
                await Task.WhenAll(drains).WaitAsync(DrainTimeout);
            }
            catch (TimeoutException)
            {
                WriteShutdownDiagnostic("Lognara agent output streams did not close before the shutdown timeout.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or
                                           OperationCanceledException or TimeoutException)
        {
            WriteShutdownDiagnostic($"Lognara agent shutdown failed: {exception.GetType().Name}.");
            try
            {
                if (!HasExited(process))
                    process.Kill();
            }
            catch (Exception killException) when (killException is InvalidOperationException or Win32Exception)
            {
                WriteShutdownDiagnostic($"Lognara agent termination failed: {killException.GetType().Name}.");
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task WatchForUnexpectedExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (Volatile.Read(ref _stopping) != 0 || !ReferenceEquals(Volatile.Read(ref _process), process))
            return;

        Environment.ExitCode = 1;
        _logger.LogCritical("Embedded Lognara agent exited unexpectedly with code {ExitCode}.", process.ExitCode);
        _lifetime.StopApplication();
    }

    private async Task WaitForReadinessAsync(Process process, Uri agentUri, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        using var readinessCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readinessCancellation.CancelAfter(ReadinessTimeout);

        while (!readinessCancellation.IsCancellationRequested)
        {
            if (HasExited(process))
                throw new InvalidOperationException("The embedded Lognara agent exited before becoming ready.");

            try
            {
                using var response = await client.GetAsync(agentUri, readinessCancellation.Token);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
                    return;
            }
            catch (HttpRequestException)
            {
                // The listener can take a short time to bind after the process starts.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout expired; retry until the overall readiness deadline.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), readinessCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("The embedded Lognara agent did not become ready within 10 seconds.");
    }

    private async Task DrainOutputAsync(StreamReader stream, bool standardError)
    {
        while (await stream.ReadLineAsync() is { } line)
        {
            if (!_options.ConsoleOutput)
                continue;

            if (standardError)
                Console.Error.WriteLine(line);
            else
                Console.Out.WriteLine(line);
        }
    }

    private static void ConfigureEnvironment(IDictionary<string, string?> environment, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(GetEnvironmentValue(environment, "LOGNARA_RELAY_TOKEN")))
            throw new InvalidOperationException("LOGNARA_RELAY_TOKEN is required when the embedded Lognara agent is enabled.");

        var relayUrl = GetEnvironmentValue(environment, "LOGNARA_RELAY_URL");
        if (string.IsNullOrWhiteSpace(relayUrl))
            relayUrl = "http://lognara-relay:7401/v1/batches";
        if (!Uri.TryCreate(relayUrl, UriKind.Absolute, out var relayUri) ||
            (relayUri.Scheme != Uri.UriSchemeHttp && relayUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(relayUri.UserInfo) ||
            !string.IsNullOrEmpty(relayUri.Fragment))
            throw new InvalidOperationException("LOGNARA_RELAY_URL must be an absolute HTTP(S) URL.");

        environment["LOGNARA_RELAY_URL"] = relayUrl;
        environment["LOGNARA_LISTEN_ADDR"] = "127.0.0.1:7400";
        environment.Remove("LOGNARA_SOURCES_FILE");
        if (string.IsNullOrWhiteSpace(GetEnvironmentValue(environment, "LOGNARA_SERVICE")))
            environment["LOGNARA_SERVICE"] = serviceName;
        if (string.IsNullOrWhiteSpace(GetEnvironmentValue(environment, "LOGNARA_SERVER")))
            environment["LOGNARA_SERVER"] = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(GetEnvironmentValue(environment, "LOGNARA_BACKEND")))
            environment["LOGNARA_BACKEND"] = "barkfluff";
    }

    private static string? GetEnvironmentValue(IDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) ? value : null;

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void SendTerminationSignal(Process process)
    {
        if (OperatingSystem.IsLinux())
        {
            if (NativeMethods.Kill(process.Id, 15) != 0 && !HasExited(process))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return;
        }

        process.CloseMainWindow();
    }

    private void WriteShutdownDiagnostic(string message)
    {
        if (_options.ConsoleOutput)
            Console.Error.WriteLine(message);
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int processId, int signal);
    }
}
