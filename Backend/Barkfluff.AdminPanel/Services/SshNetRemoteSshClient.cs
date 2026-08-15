using Barkfluff.AdminPanel.Models;

using Renci.SshNet;

namespace Barkfluff.AdminPanel.Services;

public class SshNetRemoteSshClient : IRemoteSshClient
{
    public async Task TestConnectionAsync(RemoteServer server, CancellationToken cancellationToken = default)
    {
        using var client = new SshClient(CreateConnectionInfo(server));
        await Task.Run(client.Connect, cancellationToken);
        client.Disconnect();
    }

    public async Task<RemoteSshCommandResult> RunAsync(RemoteServer server, string command, CancellationToken cancellationToken = default)
    {
        using var client = new SshClient(CreateConnectionInfo(server));
        await Task.Run(client.Connect, cancellationToken);
        try
        {
            using var sshCommand = client.RunCommand(command);
            return new RemoteSshCommandResult(sshCommand.Result, sshCommand.Error, sshCommand.ExitStatus ?? 0);
        }
        finally
        {
            client.Disconnect();
        }
    }

    public async Task<IRemoteSshShell> OpenShellAsync(RemoteServer server, CancellationToken cancellationToken = default)
    {
        var client = new SshClient(CreateConnectionInfo(server));
        try
        {
            await Task.Run(client.Connect, cancellationToken);
            var stream = client.CreateShellStream("xterm-256color", 120, 32, 0, 0, 16 * 1024);
            return new SshNetRemoteSshShell(client, stream);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static Renci.SshNet.ConnectionInfo CreateConnectionInfo(RemoteServer server)
    {
        var passwordAuth = new PasswordAuthenticationMethod(server.Username, server.Password);
        var keyboardAuth = new KeyboardInteractiveAuthenticationMethod(server.Username);
        keyboardAuth.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
                prompt.Response = server.Password;
        };

        return new Renci.SshNet.ConnectionInfo(server.Host, server.Port, server.Username, passwordAuth, keyboardAuth)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private sealed class SshNetRemoteSshShell(SshClient client, ShellStream stream) : IRemoteSshShell
    {
        private int _disposed;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, offset, count, cancellationToken);

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) =>
            stream.WriteAsync(buffer, offset, count, cancellationToken);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;

            stream.Dispose();
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
