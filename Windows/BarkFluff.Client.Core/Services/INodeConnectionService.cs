using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface INodeConnectionService
{
    Task<IReadOnlyList<PublicNode>> GetPublicNodesAsync(CancellationToken cancellationToken = default);

    Task<NodeConnectionResult> ConnectAsync(string address, CancellationToken cancellationToken = default);

    bool RestoreConnection(NodeConnection connection);
}
