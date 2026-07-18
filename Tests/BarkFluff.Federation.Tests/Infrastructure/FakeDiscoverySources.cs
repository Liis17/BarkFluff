using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Infrastructure;

public class FakeWellKnownClient : IWellKnownClient
{
    public RemoteServerDocument? Result { get; set; }
    public int CallCount { get; private set; }

    public Task<RemoteServerDocument?> FetchAsync(string servername, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(Result);
    }
}

public class FakeNavigatorClient : INavigatorClient
{
    public RemoteServerDocument? Result { get; set; }
    public int CallCount { get; private set; }

    public Task<RemoteServerDocument?> GetServerByNameAsync(string serverName, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(Result);
    }
}
