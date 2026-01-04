using BarkFluff.Proto.Onliner;
using Grpc.Core;

namespace BarkFluff.Onliner.Features.SubscribeToOnlineStatus;

public class SubscribeToOnlineStatusQuery
{
    public required List<long> UserIds { get; set; }
    public required IServerStreamWriter<UserOnlineStatus> ResponseStream { get; set; }
    public required CancellationToken CancellationToken { get; set; }
}