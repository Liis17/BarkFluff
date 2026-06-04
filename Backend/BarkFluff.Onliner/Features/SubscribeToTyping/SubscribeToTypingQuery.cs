using BarkFluff.Proto.Onliner;

using Grpc.Core;

namespace BarkFluff.Onliner.Features.SubscribeToTyping;

public class SubscribeToTypingQuery
{
    public required List<long> ChatIds { get; set; }
    public required IServerStreamWriter<TypingEvent> ResponseStream { get; set; }
    public required CancellationToken CancellationToken { get; set; }
}
