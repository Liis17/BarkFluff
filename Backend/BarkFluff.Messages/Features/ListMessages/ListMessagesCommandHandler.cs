using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Exceptions;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListMessages;

public class ListMessagesCommandHandler : IRequestHandler<ListMessagesCommand, ListMessagesResponse>
{
    private const int MaxMessagesPerRequest = 50;

    private readonly UserContext _userContext;
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly ILogger<ListMessagesCommandHandler> _logger;

    public ListMessagesCommandHandler(UserContext userContext, ChatsStorage chatsStorage, MessagesStorage messagesStorage, FilesServerApi.FilesServerApiClient filesServerApiClient,
        ILogger<ListMessagesCommandHandler> logger)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _filesServerApiClient = filesServerApiClient;
        _logger = logger;
    }


    public async Task<ListMessagesResponse> Handle(ListMessagesCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Получение списка сообщений для чата {ChatId}, пользователь {UserId}",
            request.ChatId,
            _userContext.UserId
        );

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            _logger.LogWarning(
                "Пользователь {UserId} не имеет доступа к чату {ChatId}",
                _userContext.UserId,
                request.ChatId
            );
            throw new NoAccessToChatException();
        }

        try
        {
            long? messageId = request.FromMessageId == 0 ? null : request.FromMessageId;

            List<Domain.Message> messages;

            // Check if new bi-directional pagination is requested
            if (request.OffsetBefore > 0 || request.OffsetAfter > 0)
            {
                // Clamp offsets to maximum
                var offsetBefore = Math.Min(request.OffsetBefore, MaxMessagesPerRequest);
                var offsetAfter = Math.Min(request.OffsetAfter, MaxMessagesPerRequest);

                _logger.LogDebug(
                    "Двунаправленная пагинация: Before={Before}, After={After}, FromMessageId={FromId}",
                    offsetBefore,
                    offsetAfter,
                    messageId
                );

                messages = await _messagesStorage.GetChatMessagesWithOffset(
                    request.ChatId, messageId, offsetBefore, offsetAfter);
            }
            else
            {
                // Legacy behavior for backward compatibility
                if (request.Count is 0 or > MaxMessagesPerRequest)
                {
                    _logger.LogDebug("Ограничение количества сообщений с {Count} до {Max}", request.Count, MaxMessagesPerRequest);
                    request.Count = MaxMessagesPerRequest;
                }

                _logger.LogDebug(
                    "Стандартная пагинация: Count={Count}, FromMessageId={FromId}",
                    request.Count,
                    messageId
                );

                messages = await _messagesStorage.GetChatMessages(request.ChatId, messageId, request.Count);
            }

            // �������� ���������� � ������ ��� ���������� ��������
            var fileIds = messages
                .Where(m => m.Content?.Attachments != null)
                .SelectMany(m => m.Content!.Attachments!)
                .Select(a => a.FileId)
                .Distinct()
                .ToList();

            Dictionary<string, UploadFileInfo> filesInfoMap = new();
            if (fileIds.Any())
            {
                _logger.LogDebug("Получение информации о {FileCount} файлах вложений", fileIds.Count);
                var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest { FileIds = { fileIds } });
                filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);
            }

            _logger.LogInformation(
                "Получено {MessageCount} сообщений для чата {ChatId}",
                messages.Count,
                request.ChatId
            );

            return new ListMessagesResponse()
            {
                Messages = { messages.Select(x => x.ToGrpc(filesInfoMap)) }
            };
        }
        catch (FromMessageNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Сообщение {MessageId} не найдено в чате {ChatId}",
                request.FromMessageId,
                request.ChatId
            );
            throw new MessageNotFoundException();
        }

    }
}