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
}
