using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services.SecretChats;

public interface ISecretChatGateway
{
    Task<RegisterPrekeyBundleResponse> RegisterPrekeyBundleAsync(RegisterPrekeyBundleRequest request, CancellationToken cancellationToken);
    Task<FetchPrekeyBundleResponse> FetchPrekeyBundleAsync(FetchPrekeyBundleRequest request, CancellationToken cancellationToken);
    Task<ListPeerDevicesResponse> ListPeerDevicesAsync(ListPeerDevicesRequest request, CancellationToken cancellationToken);
    Task<ReplenishOneTimePrekeysResponse> ReplenishOneTimePrekeysAsync(ReplenishOneTimePrekeysRequest request, CancellationToken cancellationToken);
    Task<RotateSignedPrekeyResponse> RotateSignedPrekeyAsync(RotateSignedPrekeyRequest request, CancellationToken cancellationToken);
    Task<GetOwnPrekeyStatusResponse> GetOwnPrekeyStatusAsync(GetOwnPrekeyStatusRequest request, CancellationToken cancellationToken);
    Task<GetCurrentDeviceResponse> GetCurrentDeviceAsync(GetCurrentDeviceRequest request, CancellationToken cancellationToken);
    Task<SendSecretChatInviteResponse> SendSecretChatInviteAsync(SendSecretChatInviteRequest request, CancellationToken cancellationToken);
    Task<AcceptSecretChatInviteResponse> AcceptSecretChatInviteAsync(AcceptSecretChatInviteRequest request, CancellationToken cancellationToken);
    Task<RejectSecretChatInviteResponse> RejectSecretChatInviteAsync(RejectSecretChatInviteRequest request, CancellationToken cancellationToken);
    Task<SendSecretMessageResponse> SendSecretMessageAsync(SendSecretMessageRequest request, CancellationToken cancellationToken);
    Task<AckSecretMessageResponse> AckSecretMessageAsync(AckSecretMessageRequest request, CancellationToken cancellationToken);
}

internal sealed class SecretChatGateway(WebApiClient api, GlobalParam parameters) : ISecretChatGateway
{
    public Task<RegisterPrekeyBundleResponse> RegisterPrekeyBundleAsync(RegisterPrekeyBundleRequest request, CancellationToken cancellationToken) =>
        api.RegisterPrekeyBundleAsync(parameters, request, cancellationToken);

    public Task<FetchPrekeyBundleResponse> FetchPrekeyBundleAsync(FetchPrekeyBundleRequest request, CancellationToken cancellationToken) =>
        api.FetchPrekeyBundleAsync(parameters, request, cancellationToken);

    public Task<ListPeerDevicesResponse> ListPeerDevicesAsync(ListPeerDevicesRequest request, CancellationToken cancellationToken) =>
        api.ListPeerDevicesAsync(parameters, request, cancellationToken);

    public Task<ReplenishOneTimePrekeysResponse> ReplenishOneTimePrekeysAsync(ReplenishOneTimePrekeysRequest request, CancellationToken cancellationToken) =>
        api.ReplenishOneTimePrekeysAsync(parameters, request, cancellationToken);

    public Task<RotateSignedPrekeyResponse> RotateSignedPrekeyAsync(RotateSignedPrekeyRequest request, CancellationToken cancellationToken) =>
        api.RotateSignedPrekeyAsync(parameters, request, cancellationToken);

    public Task<GetOwnPrekeyStatusResponse> GetOwnPrekeyStatusAsync(GetOwnPrekeyStatusRequest request, CancellationToken cancellationToken) =>
        api.GetOwnPrekeyStatusAsync(parameters, request, cancellationToken);

    public Task<GetCurrentDeviceResponse> GetCurrentDeviceAsync(GetCurrentDeviceRequest request, CancellationToken cancellationToken) =>
        api.GetCurrentDeviceAsync(parameters, request, cancellationToken);

    public Task<SendSecretChatInviteResponse> SendSecretChatInviteAsync(SendSecretChatInviteRequest request, CancellationToken cancellationToken) =>
        api.SendSecretChatInviteAsync(parameters, request, cancellationToken);

    public Task<AcceptSecretChatInviteResponse> AcceptSecretChatInviteAsync(AcceptSecretChatInviteRequest request, CancellationToken cancellationToken) =>
        api.AcceptSecretChatInviteAsync(parameters, request, cancellationToken);

    public Task<RejectSecretChatInviteResponse> RejectSecretChatInviteAsync(RejectSecretChatInviteRequest request, CancellationToken cancellationToken) =>
        api.RejectSecretChatInviteAsync(parameters, request, cancellationToken);

    public Task<SendSecretMessageResponse> SendSecretMessageAsync(SendSecretMessageRequest request, CancellationToken cancellationToken) =>
        api.SendSecretMessageAsync(parameters, request, cancellationToken);

    public Task<AckSecretMessageResponse> AckSecretMessageAsync(AckSecretMessageRequest request, CancellationToken cancellationToken) =>
        api.AckSecretMessageAsync(parameters, request, cancellationToken);
}
