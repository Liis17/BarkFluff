using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Proto.Updates;
using System.Runtime.CompilerServices;
using BarkFluff.WebApi.Core.MessengerData;
using Grpc.Core;

namespace BarkFluff.WebApi.Core;

public partial class WebApi
{
    internal sealed record SecretClientBinding(GlobalParam Parameters,
        UsersApi.UsersApiClient Users, MessagesApi.MessagesApiClient Messages, UpdatesApi.UpdatesApiClient Updates);

    internal volatile SecretClientBinding? SecretClient;

    public Task<RegisterPrekeyBundleResponse> RegisterPrekeyBundleAsync(
        GlobalParam parameters, RegisterPrekeyBundleRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.RegisterPrekeyBundleAsync(request, options).ResponseAsync);

    public Task<FetchPrekeyBundleResponse> FetchPrekeyBundleAsync(
        GlobalParam parameters, FetchPrekeyBundleRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.FetchPrekeyBundleAsync(request, options).ResponseAsync, retry: false);

    public Task<ListPeerDevicesResponse> ListPeerDevicesAsync(
        GlobalParam parameters, ListPeerDevicesRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.ListPeerDevicesAsync(request, options).ResponseAsync);

    public Task<ReplenishOneTimePrekeysResponse> ReplenishOneTimePrekeysAsync(
        GlobalParam parameters, ReplenishOneTimePrekeysRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.ReplenishOneTimePrekeysAsync(request, options).ResponseAsync);

    public Task<RotateSignedPrekeyResponse> RotateSignedPrekeyAsync(
        GlobalParam parameters, RotateSignedPrekeyRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.RotateSignedPrekeyAsync(request, options).ResponseAsync);

    public Task<GetOwnPrekeyStatusResponse> GetOwnPrekeyStatusAsync(
        GlobalParam parameters, GetOwnPrekeyStatusRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.GetOwnPrekeyStatusAsync(request, options).ResponseAsync);

    public Task<GetCurrentDeviceResponse> GetCurrentDeviceAsync(
        GlobalParam parameters, GetCurrentDeviceRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Users.GetCurrentDeviceAsync(request, options).ResponseAsync);

    public Task<SendSecretChatInviteResponse> SendSecretChatInviteAsync(
        GlobalParam parameters, SendSecretChatInviteRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Messages.SendSecretChatInviteAsync(request, options).ResponseAsync, retry: false);

    public Task<AcceptSecretChatInviteResponse> AcceptSecretChatInviteAsync(
        GlobalParam parameters, AcceptSecretChatInviteRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Messages.AcceptSecretChatInviteAsync(request, options).ResponseAsync);

    public Task<RejectSecretChatInviteResponse> RejectSecretChatInviteAsync(
        GlobalParam parameters, RejectSecretChatInviteRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Messages.RejectSecretChatInviteAsync(request, options).ResponseAsync);

    public Task<SendSecretMessageResponse> SendSecretMessageAsync(
        GlobalParam parameters, SendSecretMessageRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Messages.SendSecretMessageAsync(request, options).ResponseAsync, retry: false);

    public Task<AckSecretMessageResponse> AckSecretMessageAsync(
        GlobalParam parameters, AckSecretMessageRequest request, CancellationToken cancellationToken) =>
        CallSecretRpcAsync(parameters, cancellationToken, (binding, options) =>
            binding.Messages.AckSecretMessageAsync(request, options).ResponseAsync);

    public IAsyncEnumerable<SecretChatInviteEvent> ReadSecretChatInvitesAsync(GlobalParam parameters, CancellationToken token) =>
        ReadSecretStreamAsync(parameters, (updates, ct) => updates.SubscribeSecretChatInvites(new(), cancellationToken: ct), token);

    public IAsyncEnumerable<SecretChatInviteResolutionEvent> ReadSecretChatResolutionsAsync(GlobalParam parameters, CancellationToken token) =>
        ReadSecretStreamAsync(parameters, (updates, ct) => updates.SubscribeSecretChatResolutions(new(), cancellationToken: ct), token);

    public IAsyncEnumerable<NewSecretMessageEvent> ReadSecretMessagesAsync(GlobalParam parameters, CancellationToken token) =>
        ReadSecretStreamAsync(parameters, (updates, ct) => updates.SubscribeSecretMessages(new(), cancellationToken: ct), token);

    private async IAsyncEnumerable<T> ReadSecretStreamAsync<T>(GlobalParam parameters,
        Func<UpdatesApi.UpdatesApiClient, CancellationToken, AsyncServerStreamingCall<T>> subscribe,
        [EnumeratorCancellation] CancellationToken token)
    {
        var binding = SecretClient;
        var userId = parameters.UserId;
        if (binding is null || !ReferenceEquals(binding.Parameters, parameters))
            throw new OperationCanceledException(token);
        var valid = await EnsureTokenValidAsync(parameters).WaitAsync(token);
        if (!valid.IsSuccess)
            throw new RpcException(new Status(StatusCode.Unauthenticated, string.Empty));

        using var refresh = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Restart(object? sender, EventArgs args)
        {
            try { refresh.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        TokenRefreshed += Restart;
        try
        {
            CheckBinding();
            using var call = subscribe(binding.Updates, refresh.Token);
            while (await MoveNextAsync(call))
            {
                CheckBinding();
                yield return call.ResponseStream.Current;
            }
        }
        finally
        {
            TokenRefreshed -= Restart;
        }

        void CheckBinding()
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(SecretClient, binding) || userId != parameters.UserId)
                throw new OperationCanceledException(token);
        }

        async Task<bool> MoveNextAsync(AsyncServerStreamingCall<T> call)
        {
            try
            {
                return await call.ResponseStream.MoveNext(refresh.Token);
            }
            catch (RpcException error) when (error.StatusCode == StatusCode.Unauthenticated && !token.IsCancellationRequested)
            {
                CheckBinding();
                await ForceRefreshTokenAsync(parameters).WaitAsync(token);
                throw;
            }
        }
    }

    private async Task<T> CallSecretRpcAsync<T>(GlobalParam parameters, CancellationToken cancellationToken,
        Func<SecretClientBinding, CallOptions, Task<T>> call, bool retry = true) where T : class
    {
        var userId = parameters.UserId;
        SecretClientBinding Current()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = SecretClient;
            return binding is not null && ReferenceEquals(binding.Parameters, parameters) && parameters.UserId == userId
                ? binding : throw new InvalidOperationException("The secret chat session is unavailable.");
        }

        _ = Current();
        // Эти RPC расходуют одноразовый ключ или создают серверное событие с новым ID.
        if (!retry && !(await TokenManager.EnsureTokenValidAsync(parameters).WaitAsync(cancellationToken)).IsSuccess)
            throw new InvalidOperationException("The secret chat session could not be authorized.");

        var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: cancellationToken);
        var response = await TokenManager.ExecuteWithTokenRefresh(parameters, () => call(Current(), options), retry)
            .WaitAsync(cancellationToken);
        _ = Current();
        return response ?? throw new InvalidOperationException("The secret chat RPC returned no result.");
    }
}
