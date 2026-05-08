namespace BarkFluff.Updates.Host;

using BarkFluff.GrpcServer.Metrics;

using Features.SubscribeNewMessages;

using Grpc.Core;

using GrpcServer.XAuth;

using Microsoft.AspNetCore.Authorization;

using Proto.Updates;

using Shared.Identity;

using System.Threading;
using System.Threading.Tasks;

[Authorize(Policy = nameof(TokenType.User))]
public class UpdatesApiService : BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiBase
{
    private readonly UserContext _userContext;
    private readonly StreamSubscriptionsManager _newMessagesSubscriptionsManager;
    private readonly Features.SubscribeMessagesRead.StreamSubscriptionsManager _newReadBySubscriptionsManager;
    private readonly Features.SubscribeMessagesEdited.StreamSubscriptionsManager _editedSubscriptionsManager;
    private readonly Features.SubscribeMessagesDeleted.StreamSubscriptionsManager _deletedSubscriptionsManager;
    private readonly MetricsCollector _metrics;

    public UpdatesApiService(
        UserContext userContext,
        StreamSubscriptionsManager newMessagesSubscriptionsManager,
        Features.SubscribeMessagesRead.StreamSubscriptionsManager newReadBySubscriptionsManager,
        Features.SubscribeMessagesEdited.StreamSubscriptionsManager editedSubscriptionsManager,
        Features.SubscribeMessagesDeleted.StreamSubscriptionsManager deletedSubscriptionsManager,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _newMessagesSubscriptionsManager = newMessagesSubscriptionsManager;
        _newReadBySubscriptionsManager = newReadBySubscriptionsManager;
        _editedSubscriptionsManager = editedSubscriptionsManager;
        _deletedSubscriptionsManager = deletedSubscriptionsManager;
        _metrics = metrics;
    }

    private long TotalActive =>
        _newMessagesSubscriptionsManager.ActiveCount
        + _newReadBySubscriptionsManager.ActiveCount
        + _editedSubscriptionsManager.ActiveCount
        + _deletedSubscriptionsManager.ActiveCount;

    public override async Task SubscribeNewMessages(
        SubscribeNewMessagesRequest request,
        IServerStreamWriter<NewMessageEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _newMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("new_messages_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("new_messages_subscriptions_active", _newMessagesSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _newMessagesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("new_messages_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("new_messages_subscriptions_active", _newMessagesSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesRead(SubscribeMessagesReadRequest request, IServerStreamWriter<MessageReadEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _newReadBySubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("read_by_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("read_by_subscriptions_active", _newReadBySubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _newReadBySubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("read_by_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("read_by_subscriptions_active", _newReadBySubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesEdited(SubscribeMessagesEditedRequest request,
        IServerStreamWriter<MessageEditedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _editedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("messages_edited_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("messages_edited_subscriptions_active", _editedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _editedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("messages_edited_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("messages_edited_subscriptions_active", _editedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }

    public override async Task SubscribeMessagesDeleted(SubscribeMessagesDeletedRequest request,
        IServerStreamWriter<MessageDeletedEvent> responseStream, ServerCallContext context)
    {
        long userId = _userContext.UserId;

        var subscriptionId = _deletedSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("messages_deleted_subscriptions_opened");
        _metrics.Increment("active_subscriptions"); // обратная совместимость
        _metrics.Set("messages_deleted_subscriptions_active", _deletedSubscriptionsManager.ActiveCount);
        _metrics.Set("subscriptions_active_total", TotalActive);

        try
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _deletedSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("messages_deleted_subscriptions_closed");
            _metrics.Increment("active_subscriptions_removed"); // обратная совместимость
            _metrics.Set("messages_deleted_subscriptions_active", _deletedSubscriptionsManager.ActiveCount);
            _metrics.Set("subscriptions_active_total", TotalActive);
        }
    }
}
