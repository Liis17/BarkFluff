using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public sealed class ClientSession : IClientSession
{
    public NodeConnection? CurrentConnection { get; private set; }

    public void SetConnection(NodeConnection connection)
    {
        CurrentConnection = connection;
    }
}
