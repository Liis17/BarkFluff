using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface IClientSession
{
    NodeConnection? CurrentConnection { get; }

    void SetConnection(NodeConnection connection);
}
