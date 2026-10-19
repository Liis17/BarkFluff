using System.Diagnostics;

using MassTransit.Logging;

using Microsoft.Extensions.Hosting;

namespace BarkFluff.GrpcServer.Tracing;

/// <summary>Enables causal propagation without collecting or exporting span payloads.</summary>
public sealed class TraceDiagnostics : IHostedService, IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name is DiagnosticHeaders.DefaultListenerName or
            "Microsoft.AspNetCore" or "System.Net.Http" or "Grpc.Net.Client",
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
        SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.PropagationData,
    };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _listener.Dispose();
}
