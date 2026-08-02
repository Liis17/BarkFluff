using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface INodeConnectionService
{
    Task<IReadOnlyList<PublicNode>> GetPublicNodesAsync(CancellationToken cancellationToken = default);

    Task<NodeConnectionResult> ConnectAsync(string address, CancellationToken cancellationToken = default);

    bool RestoreConnection(NodeConnection connection);
}
