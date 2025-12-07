namespace BarkFluff.Updates.Host;

using System.Threading;
using System.Threading.Tasks;
using Features.SubscribeNewMessages;
using Features.SubscribeReadReceipts;
using Grpc.Core;
using GrpcServer.XAuth;
using Microsoft.AspNetCore.Authorization;
using Proto.Updates;
using Shared.Identity;

[Authorize(Policy = nameof(TokenType.User))]
public class UpdatesApiService : BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiBase
{
    private readonly UserContext _userContext;
    private readonly StreamSubscriptionsManager _subscriptionsManager;
    private readonly ReadReceiptSubscriptionsManager _readReceiptSubscriptionsManager;

    public UpdatesApiService(
        UserContext userContext,
        StreamSubscriptionsManager subscriptionsManager,
        ReadReceiptSubscriptionsManager readReceiptSubscriptionsManager)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
        _readReceiptSubscriptionsManager = readReceiptSubscriptionsManager;
    }
    
    public override async Task SubscribeNewMessages(
        SubscribeNewMessagesRequest request, 
        IServerStreamWriter<NewMessageEvent> responseStream,
        ServerCallContext context)
    {
        
        long userId = _userContext.UserId;
        
        // Регистрируем подписку
        _subscriptionsManager.RegisterSubscription(userId, responseStream);
        
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
            // Удаляем подписку при завершении
            _subscriptionsManager.RemoveSubscription(userId, responseStream);
        }
    }

    public override async Task SubscribeToReadReceipts(
        SubscribeReadReceiptsRequest request,
        IServerStreamWriter<ReadReceiptUpdate> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;

        // Регистрируем подписку
        if (string.IsNullOrEmpty(request.ChatId) || request.LastMessageOnly)
        {
            // Global subscription for chat list (last message only)
            _readReceiptSubscriptionsManager.RegisterGlobalSubscription(userId, responseStream);
        }
        else
        {
            // Chat-specific subscription for open chat
            _readReceiptSubscriptionsManager.RegisterChatSubscription(userId, request.ChatId, responseStream);
        }

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
            // Удаляем подписку при завершении
            if (string.IsNullOrEmpty(request.ChatId) || request.LastMessageOnly)
            {
                _readReceiptSubscriptionsManager.RemoveGlobalSubscription(userId, responseStream);
            }
            else
            {
                _readReceiptSubscriptionsManager.RemoveChatSubscription(userId, request.ChatId, responseStream);
            }
        }
    }
}