using BarkFluff.Calls.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Calls;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Calls.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class CallsApiService : CallsApi.CallsApiBase
{
    private readonly CallsService _calls;
    private readonly CallEventSubscriptionsManager _subscriptions;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;

    public CallsApiService(
        CallsService calls,
        CallEventSubscriptionsManager subscriptions,
        UserContext userContext,
        MetricsCollector metrics)
    {
        _calls = calls;
        _subscriptions = subscriptions;
        _userContext = userContext;
        _metrics = metrics;
    }

    public override Task<InitiateCallResponse> InitiateCall(InitiateCallRequest request, ServerCallContext context)
        => _calls.InitiateAsync(request, context.CancellationToken);

    public override Task<JoinCallResponse> JoinCall(JoinCallRequest request, ServerCallContext context)
        => _calls.JoinAsync(request.CallId, context.CancellationToken);

    public override Task<AcceptCallResponse> AcceptCall(AcceptCallRequest request, ServerCallContext context)
        => _calls.AcceptAsync(request.CallId, context.CancellationToken);

    public override Task<RejectCallResponse> RejectCall(RejectCallRequest request, ServerCallContext context)
        => _calls.RejectAsync(request.CallId, context.CancellationToken);

    public override Task<EndCallResponse> EndCall(EndCallRequest request, ServerCallContext context)
        => _calls.EndAsync(request.CallId, context.CancellationToken);

    public override async Task SubscribeCallEvents(
        SubscribeCallEventsRequest request,
        IServerStreamWriter<CallEvent> responseStream,
        ServerCallContext context)
    {
        var userId = _userContext.UserId;
        var deviceId = RequireDeviceId();

        var subscriptionId = _subscriptions.RegisterSubscription(userId, deviceId, responseStream);
        _metrics.Increment("call_events_subscriptions_opened");
        _metrics.Set("call_events_subscriptions_active", _subscriptions.ActiveCount);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _subscriptions.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("call_events_subscriptions_closed");
            _metrics.Set("call_events_subscriptions_active", _subscriptions.ActiveCount);
        }
    }

    private Guid RequireDeviceId()
    {
        if (string.IsNullOrEmpty(_userContext.DeviceId) || !Guid.TryParse(_userContext.DeviceId, out var deviceId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Для подписки на звонки требуется device-id в токене"));
        }

        return deviceId;
    }
}
