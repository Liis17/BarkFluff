namespace Barkfluff.AdminPanel.Services;

public interface IDockerCommandRunner
{
    Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments);
}
