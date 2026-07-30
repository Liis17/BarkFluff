using BarkFluff.Federation.Features.FederationS2SApi;
using BarkFluff.Proto.Federation;

using Grpc.Core;

namespace BarkFluff.Federation.Host;

public sealed class FederationS2SApiService(FederationS2SApiHandler handler)
    : FederationS2SApi.FederationS2SApiBase
{
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
        => handler.Ping(request, context);

    public override Task SubscribePresence(SubscribePresenceRequest request, IServerStreamWriter<PresenceEvent> responseStream, ServerCallContext context)
        => handler.SubscribePresence(request, responseStream, context);

    public override Task FetchFile(FetchFileRequest request, IServerStreamWriter<FetchFileChunk> responseStream, ServerCallContext context)
        => handler.FetchFile(request, responseStream, context);

    public override Task<DeliverTypingResponse> DeliverTyping(DeliverTypingRequest request, ServerCallContext context)
        => handler.DeliverTyping(request, context);

    public override Task<GetServerKeysResponse> GetServerKeys(GetServerKeysRequest request, ServerCallContext context)
        => handler.GetServerKeys(request, context);

    public override Task<GetUserProfileResponse> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
        => handler.GetUserProfile(request, context);

    public override Task<DeliverEventsResponse> DeliverEvents(DeliverEventsRequest request, ServerCallContext context)
        => handler.DeliverEvents(request, context);
}
