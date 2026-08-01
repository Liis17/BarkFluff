using Barkfluff.AdminPanel.Models;

namespace Barkfluff.AdminPanel.Services;

public record RemoteSshCommandResult(string Stdout, string Stderr, int ExitCode);

public interface IRemoteSshClient
{
    Task TestConnectionAsync(RemoteServer server, CancellationToken cancellationToken = default);
    Task<RemoteSshCommandResult> RunAsync(RemoteServer server, string command, CancellationToken cancellationToken = default);
}
