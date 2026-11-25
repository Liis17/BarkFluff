using BarkFluff.Proto.Calls;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;
using BarkFluff.WebApi.Core.MessengerData;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services.Calls;

public interface ICallSignaling
{
    long UserId { get; }
    bool IsCurrent { get; }
    bool IsAdvertised { get; }
    event EventHandler? TokenRefreshed;
    Task<InitiateCallResponse> InitiateAsync(InitiateCallRequest request, CancellationToken ct);
    Task<JoinCallResponse> JoinAsync(string id, CancellationToken ct);
    Task<GetActiveCallsResponse> GetActiveAsync(string chatId, CancellationToken ct);
    Task<ListCallHistoryResponse> ListHistoryAsync(ListCallHistoryRequest request, CancellationToken ct);
    Task<SetCallAudioQualityResponse> SetAudioQualityAsync(string id, CallAudioQuality quality, CancellationToken ct);
    Task<AcceptCallResponse> AcceptAsync(string id, CancellationToken ct);
    Task FinishAsync(string id, bool reject, CancellationToken ct);
    Task<IAsyncEnumerable<CallEvent>?> SubscribeAsync(CancellationToken ct);
}

public sealed class CallSignaling(WebApiClient api, IClientSession session)
{
    public ICallSignaling? Open() => session.CurrentConnection?.ConnectionParameters is { } parameters
        ? new Connection(api, session, parameters) : null;

    private sealed class Connection(WebApiClient api, IClientSession session, GlobalParam parameters) : ICallSignaling
    {
        private readonly long _userId = parameters.UserId;
        public long UserId => _userId;
        public bool IsCurrent => ReferenceEquals(session.CurrentConnection?.ConnectionParameters, parameters)
            && parameters.UserId == _userId;
        public bool IsAdvertised => IsCurrent && api.CallsAvailable;

        public event EventHandler? TokenRefreshed
        {
            add => api.TokenRefreshed += value;
            remove => api.TokenRefreshed -= value;
        }

        public async Task<InitiateCallResponse> InitiateAsync(InitiateCallRequest request, CancellationToken ct)
        {
            EnsureCurrent(ct);
            if ((await api.EnsureTokenValidAsync(parameters)).IsSuccess)
            {
                EnsureCurrent(ct);
                return await api.InitiateCallAsync(request, parameters, ct);
            }
            throw new RpcException(new Status(StatusCode.Unauthenticated, string.Empty));
        }

        public Task<JoinCallResponse> JoinAsync(string id, CancellationToken ct)
        {
            EnsureCurrent(ct);
            return api.JoinCallAsync(id, parameters, ct);
        }

        public Task<GetActiveCallsResponse> GetActiveAsync(string chatId, CancellationToken ct)
        {
            EnsureCurrent(ct);
            return api.GetActiveCallsAsync(chatId, parameters, ct);
        }

        public Task<ListCallHistoryResponse> ListHistoryAsync(ListCallHistoryRequest request, CancellationToken ct)
        {
            EnsureCurrent(ct);
            return api.ListCallHistoryAsync(request, parameters, ct);
        }

        public Task<SetCallAudioQualityResponse> SetAudioQualityAsync(string id, CallAudioQuality quality, CancellationToken ct)
        {
            EnsureCurrent(ct);
            return api.SetCallAudioQualityAsync(id, quality, parameters, ct);
        }

        public Task<AcceptCallResponse> AcceptAsync(string id, CancellationToken ct)
        {
            EnsureCurrent(ct);
            return api.AcceptCallAsync(id, parameters, ct);
        }

        public Task FinishAsync(string id, bool reject, CancellationToken ct)
        {
            EnsureCurrent(ct);
            return api.FinishCallAsync(id, reject, parameters, ct);
        }

        public Task<IAsyncEnumerable<CallEvent>?> SubscribeAsync(CancellationToken ct)
        {
            EnsureCurrent(ct);
            return Task.FromResult<IAsyncEnumerable<CallEvent>?>(api.ReadCallEventsAsync(parameters, ct));
        }

        private void EnsureCurrent(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent)
                throw new OperationCanceledException();
        }
    }
}
