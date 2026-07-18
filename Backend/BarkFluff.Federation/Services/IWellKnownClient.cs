namespace BarkFluff.Federation.Services;

public interface IWellKnownClient
{
    Task<RemoteServerDocument?> FetchAsync(string servername, CancellationToken ct = default);
}
