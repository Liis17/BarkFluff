using BarkFluff.Proto.FastAuth;

using Grpc.Core;

namespace BarkFluff.FastAuth.Features.SubscribeFastAuthResult;

public class SubscribeFastAuthResultQuery
{
    public required string FastAuthId { get; init; }

    public required IServerStreamWriter<FastAuthResult> ResponseStream { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}
