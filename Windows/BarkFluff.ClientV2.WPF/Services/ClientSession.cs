using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public sealed class ClientSession : IClientSession
{
    public NodeConnection? CurrentConnection { get; private set; }

    public void SetConnection(NodeConnection connection)
    {
        CurrentConnection = connection;
    }
}
