using Barkfluff.AdminPanel.Models;

namespace Barkfluff.AdminPanel.Services;

public record RemoteSshCommandResult(string Stdout, string Stderr, int ExitCode);

public interface IRemoteSshShell : IAsyncDisposable
{
    Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
    Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
}

public interface IRemoteSshClient
{
    Task TestConnectionAsync(RemoteServer server, CancellationToken cancellationToken = default);
    Task<RemoteSshCommandResult> RunAsync(RemoteServer server, string command, CancellationToken cancellationToken = default);
    Task<IRemoteSshShell> OpenShellAsync(RemoteServer server, CancellationToken cancellationToken = default);
}
