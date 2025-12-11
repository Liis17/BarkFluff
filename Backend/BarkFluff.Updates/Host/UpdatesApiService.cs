namespace BarkFluff.Updates.Host;

using System.Threading;
using System.Threading.Tasks;
using Features.SubscribeNewMessages;
using Grpc.Core;
using GrpcServer.XAuth;
using Microsoft.AspNetCore.Authorization;
using Proto.Updates;
using Shared.Identity;

[Authorize(Policy = nameof(TokenType.User))]
public class UpdatesApiService : BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiBase
{
    private readonly UserContext _userContext;
    private readonly StreamSubscriptionsManager _newMessagesSubscriptionsManager;
    private readonly Features.SubscribeMessagesRead.StreamSubscriptionsManager _newReadBySubscriptionsManager;

    public UpdatesApiService(
        UserContext userContext,
        StreamSubscriptionsManager newMessagesSubscriptionsManager, Features.SubscribeMessagesRead.StreamSubscriptionsManager newReadBySubscriptionsManager)
    {
        _userContext = userContext;
        _newMessagesSubscriptionsManager = newMessagesSubscriptionsManager;
        _newReadBySubscriptionsManager = newReadBySubscriptionsManager;
    }
    
    public override async Task SubscribeNewMessages(
        SubscribeNewMessagesRequest request, 
        IServerStreamWriter<NewMessageEvent> responseStream,
        ServerCallContext context)
    {
        
        long userId = _userContext.UserId;
        
        // Регистрируем подписку
        _newMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
        
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
            _newMessagesSubscriptionsManager.RemoveSubscription(userId, responseStream);
        }
    }

    public override async Task SubscribeMessagesRead(SubscribeMessagesReadRequest request, IServerStreamWriter<MessageReadEvent> responseStream,
        ServerCallContext context)
    {
        long userId = _userContext.UserId;
        
        // Регистрируем подписку
        _newReadBySubscriptionsManager.RegisterSubscription(userId, responseStream);
        
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
            _newReadBySubscriptionsManager.RemoveSubscription(userId, responseStream);
        }
    }
}