using BarkFluff.Federation.Features.FederationInternalApi;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Federation.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public sealed class FederationInternalApiService(FederationInternalApiHandler handler)
    : FederationInternalApi.FederationInternalApiBase
{
    public override Task<SetPresenceInterestResponse> SetPresenceInterest(SetPresenceInterestRequest request, ServerCallContext context)
        => handler.SetPresenceInterest(request, context);

    public override Task FetchRemoteFile(FetchRemoteFileRequest request, IServerStreamWriter<FetchFileChunk> responseStream, ServerCallContext context)
        => handler.FetchRemoteFile(request, responseStream, context);

    public override Task<DeliverTypingOutboundResponse> DeliverTypingOutbound(DeliverTypingOutboundRequest request, ServerCallContext context)
        => handler.DeliverTypingOutbound(request, context);

    public override Task<RotateSigningKeyResponse> RotateSigningKey(RotateSigningKeyRequest request, ServerCallContext context)
        => handler.RotateSigningKey(request, context);

    public override Task<GetKnownServersResponse> GetKnownServers(GetKnownServersRequest request, ServerCallContext context)
        => handler.GetKnownServers(request, context);

    public override Task<UpsertManualPeerResponse> UpsertManualPeer(UpsertManualPeerRequest request, ServerCallContext context)
        => handler.UpsertManualPeer(request, context);

    public override Task<SetServerBlockedResponse> SetServerBlocked(SetServerBlockedRequest request, ServerCallContext context)
        => handler.SetServerBlocked(request, context);

    public override Task<GetFederationStatusResponse> GetFederationStatus(GetFederationStatusRequest request, ServerCallContext context)
        => handler.GetFederationStatus(request, context);

    public override Task<ResolveRemoteUserResponse> ResolveRemoteUser(ResolveRemoteUserRequest request, ServerCallContext context)
        => handler.ResolveRemoteUser(request, context);

    public override Task<EnqueueOutboundResponse> EnqueueOutbound(EnqueueOutboundRequest request, ServerCallContext context)
        => handler.EnqueueOutbound(request, context);
}
