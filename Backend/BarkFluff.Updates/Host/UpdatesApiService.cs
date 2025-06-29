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
    private readonly StreamSubscriptionsManager _subscriptionsManager;

    public UpdatesApiService(
        UserContext userContext,
        StreamSubscriptionsManager subscriptionsManager)
    {
        _userContext = userContext;
        _subscriptionsManager = subscriptionsManager;
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
}