using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.PostCallSystemMessage;

/// <summary>
/// Пишет системное сообщение об итоге звонка в чат и рассылает его участникам
/// (через тот же путь, что обычные сообщения → Updates стримит клиентам).
/// Для личного звонка пишет в существующий личный чат; если чата ещё нет —
/// не создаёт его (posted = false).
/// </summary>
public class PostCallSystemMessageCommandHandler
    : IRequestHandler<PostCallSystemMessageCommand, PostCallSystemMessageResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly ILogger<PostCallSystemMessageCommandHandler> _logger;

    public PostCallSystemMessageCommandHandler(
        ChatsStorage chatsStorage,
        MessagesStorage messagesStorage,
        MessageQueueSender messageQueueSender,
        ILogger<PostCallSystemMessageCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _messageQueueSender = messageQueueSender;
        _logger = logger;
    }

    public async Task<PostCallSystemMessageResponse> Handle(
        PostCallSystemMessageCommand request,
        CancellationToken cancellationToken)
    {
        Guid chatId;

        if (request.ChatId is { } groupChatId)
        {
            chatId = groupChatId;
        }
        else
        {
            // Личный звонок: пишем в уже существующий личный чат, не создаём новый.
            var existing = await _chatsStorage.GetUserChatIdWithPerson(
                request.CalleeUserId!.Value, request.CallerUserId!.Value);

            if (existing is null)
            {
                _logger.LogDebug(
                    "Личного чата между {Caller} и {Callee} нет — системное сообщение о звонке не записано",
                    request.CallerUserId, request.CalleeUserId);
                return new PostCallSystemMessageResponse { Posted = false };
            }

            chatId = existing.Value;
        }

        var systemMessage = new Domain.Message
        {
            ChatId = chatId,
            Content = new Domain.MessageContent { Text = ComposeText(request.Result, request.DurationSeconds) },
            ReadBy = [request.SenderUserId],
            SenderId = request.SenderUserId,
            SentAt = DateTime.UtcNow,
            Type = Domain.MessageContentType.System
        };

        systemMessage = await _messagesStorage.AddMessage(systemMessage);

        var members = await _chatsStorage.GetChatMembers(chatId, 0, int.MaxValue);
        await _messageQueueSender.SendMessage(systemMessage, chatId, members.LocalUserIds());

        _logger.LogInformation("Системное сообщение о звонке записано в чат {ChatId}", chatId);
        return new PostCallSystemMessageResponse { Posted = true };
    }

    private static string ComposeText(CallSystemResult result, long durationSeconds) => result switch
    {
        CallSystemResult.Missed => "Пропущенный звонок",
        CallSystemResult.Rejected => "Звонок отклонён",
        CallSystemResult.Ended => $"Звонок · {durationSeconds / 60}:{durationSeconds % 60:D2}",
        _ => "Звонок"
    };
}
