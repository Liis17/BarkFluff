using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Infrastructure;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders;

/// <summary>
/// После успешной команды <see cref="IChatFoldersMutation"/> публикует <c>ChatFoldersChanged</c>,
/// чтобы Updates разослал сигнал всем устройствам пользователя.
/// </summary>
/// <remarks>
/// Публикация идёт после записи в БД без outbox: при сбое RabbitMQ изменение сохранится,
/// а другие устройства увидят его при следующем перечитывании папок (старт, переподключение).
/// Поэтому ошибка публикации только логируется и не превращает успешную команду в ошибку.
/// </remarks>
public class ChatFoldersChangedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly UserContext _userContext;
    private readonly UserInfoQueueSender _queueSender;
    private readonly ILogger<ChatFoldersChangedBehavior<TRequest, TResponse>> _logger;

    public ChatFoldersChangedBehavior(
        UserContext userContext,
        UserInfoQueueSender queueSender,
        ILogger<ChatFoldersChangedBehavior<TRequest, TResponse>> logger)
    {
        _userContext = userContext;
        _queueSender = queueSender;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is not IChatFoldersMutation)
        {
            return response;
        }

        try
        {
            await _queueSender.ChatFoldersChangedEvent(_userContext.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось опубликовать изменение папок чатов пользователя {UserId}", _userContext.UserId);
        }

        return response;
    }
}
