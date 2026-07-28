using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface IClientSession
{
    NodeConnection? CurrentConnection { get; }

    void SetConnection(NodeConnection connection);
}
