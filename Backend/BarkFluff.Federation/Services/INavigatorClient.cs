namespace BarkFluff.Federation.Services;

public interface INavigatorClient
{
    Task<RemoteServerDocument?> GetServerByNameAsync(string serverName, CancellationToken ct = default);
}
