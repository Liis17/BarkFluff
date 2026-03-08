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
    private readonly MetricsCollector _metrics;

    public UpdatesApiService(
        UserContext userContext,
        StreamSubscriptionsManager newMessagesSubscriptionsManager,
        Features.SubscribeMessagesRead.StreamSubscriptionsManager newReadBySubscriptionsManager,
        MetricsCollector metrics)
    {
        _userContext = userContext;
        _newMessagesSubscriptionsManager = newMessagesSubscriptionsManager;
        _newReadBySubscriptionsManager = newReadBySubscriptionsManager;
        _metrics = metrics;
    }

    public override async Task SubscribeNewMessages(
        SubscribeNewMessagesRequest request,
        IServerStreamWriter<NewMessageEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        // Регистрируем подписку и получаем уникальный идентификатор
        var subscriptionId = _newMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("active_subscriptions");

        try
        {
            // Ждем отмены запроса (например, при отключении клиента)
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение при отмене запроса
        }
        finally
        {
            // Удаляем подписку при завершении по идентификатору
            _newMessagesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("active_subscriptions_removed");
        }
    }

    public override async Task SubscribeMessagesRead(SubscribeMessagesReadRequest request, IServerStreamWriter<MessageReadEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        // Регистрируем подписку и получаем уникальный идентификатор
        var subscriptionId = _newReadBySubscriptionsManager.RegisterSubscription(userId, responseStream);
        _metrics.Increment("active_subscriptions");

        try
        {
            // Ждем отмены запроса (например, при отключении клиента)
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение при отмене запроса
        }
        finally
        {
            // Удаляем подписку при завершении по идентификатору
            _newReadBySubscriptionsManager.RemoveSubscription(userId, subscriptionId);
            _metrics.Increment("active_subscriptions_removed");
        }
    }
}