using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.PinMessage;

public class PinMessageCommandHandler : IRequestHandler<PinMessageCommand, PinMessageResponse>
{
    private const int MaxPinnedPerChat = 100;

    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<PinMessageCommandHandler> _logger;

    public PinMessageCommandHandler(PinnedMessagesStorage pinnedMessagesStorage, MessagesStorage messagesStorage,
        ChatsStorage chatsStorage, FilesServerApi.FilesServerApiClient filesServerApiClient,
        UsersServerApi.UsersServerApiClient usersServerApiClient, UserContext userContext,
        MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<PinMessageCommandHandler> logger)
    {
        _pinnedMessagesStorage = pinnedMessagesStorage;
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _filesServerApiClient = filesServerApiClient;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<PinMessageResponse> Handle(PinMessageCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Закрепление сообщения {MessageId} в чате {ChatId} пользователем {UserId}",
            request.MessageId,
            request.ChatId,
            _userContext.UserId
        );

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var message = await _messagesStorage.GetMessageById(request.MessageId);

        if (message is null || message.ChatId != request.ChatId || message.IsDeleted)
        {
            throw new MessageNotFoundException();
        }

        var existing = await _pinnedMessagesStorage.GetPinByMessageIdAsync(request.ChatId, request.MessageId);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Сообщение {MessageId} уже закреплено в чате {ChatId} — idempotent no-op",
                request.MessageId,
                request.ChatId
            );

            var filesInfoMapExisting = await LoadFilesInfoAsync(message);
            return new PinMessageResponse { Pinned = existing.ToGrpc(message, filesInfoMapExisting) };
        }

        var totalPinned = await _pinnedMessagesStorage.CountByChatAsync(request.ChatId);

        if (totalPinned >= MaxPinnedPerChat)
        {
            _logger.LogWarning(
                "Достигнут лимит закреплённых сообщений в чате {ChatId}: {MaxPinnedPerChat}",
                request.ChatId,
                MaxPinnedPerChat
            );
            throw new TooManyPinnedMessagesException();
        }

        var pin = new Domain.PinnedMessage
        {
            ChatId = request.ChatId,
            MessageId = request.MessageId,
            PinnerUserId = _userContext.UserId,
            PinnedAt = DateTime.UtcNow
        };

        await _pinnedMessagesStorage.AddAsync(pin);
        await _pinnedMessagesStorage.SaveChangesAsync();

        var members = await _chatsStorage.GetChatMembers(request.ChatId, 0, int.MaxValue);
        var memberIds = members.LocalUserIds();

        var pinnerName = await GetUserDisplayNameAsync(_userContext.UserId);

        var systemMessage = new Domain.Message
        {
            ChatId = request.ChatId,
            Content = new MessageContent
            {
                Text = $"Пользователь {pinnerName} закрепил сообщение"
            },
            ReadBy = [_userContext.UserId],
            SenderId = _userContext.UserId,
            SentAt = DateTime.UtcNow,
            Type = MessageContentType.System
        };

        systemMessage = await _messagesStorage.AddMessage(systemMessage);

        await _messageQueueSender.SendMessage(systemMessage, request.ChatId, memberIds);
        await _messageQueueSender.SendPinned(request.ChatId, request.MessageId, _userContext.UserId, pin.PinnedAt, memberIds);

        _metrics.Increment("messages_pinned");

        var filesInfoMap = await LoadFilesInfoAsync(message);

        return new PinMessageResponse { Pinned = pin.ToGrpc(message, filesInfoMap) };
    }

    private async Task<Dictionary<string, UploadFileInfo>?> LoadFilesInfoAsync(Domain.Message message)
    {
        var fileIds = CollectFileIds(message);

        if (fileIds.Count == 0)
        {
            return null;
        }

        var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest
        {
            FileIds = { fileIds }
        });

        return filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);
    }

    private static List<string> CollectFileIds(Domain.Message message)
    {
        var fileIds = new HashSet<string>();

        if (message.Content?.Attachments is null)
        {
            return [];
        }

        foreach (var attachment in message.Content.Attachments)
        {
            // Federated-вложения рендерятся из снапшота (этап 3.1) — Files не дёргаем.
            if (attachment.OriginServer is not null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(attachment.FileId))
            {
                fileIds.Add(attachment.FileId);
            }

            if (attachment.ForwardedAttachments is null)
            {
                continue;
            }

            foreach (var forwarded in attachment.ForwardedAttachments)
            {
                if (!string.IsNullOrEmpty(forwarded.FileId))
                {
                    fileIds.Add(forwarded.FileId);
                }
            }
        }

        return fileIds.ToList();
    }

    private async Task<string> GetUserDisplayNameAsync(long userId)
    {
        try
        {
            var response = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = userId });
            return $"{response.User.FirstName} {response.User.LastName}".Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить имя пользователя {UserId}", userId);
            return $"Пользователь {userId}";
        }
    }
}
