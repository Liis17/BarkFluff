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
    private readonly FederatedReadStatesStorage _federatedReadStatesStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly ReplyPreviewResolver _replyPreviewResolver;
    private readonly ILogger<ListMessagesCommandHandler> _logger;

    public ListMessagesCommandHandler(UserContext userContext, ChatsStorage chatsStorage, MessagesStorage messagesStorage,
        FederatedReadStatesStorage federatedReadStatesStorage, FilesServerApi.FilesServerApiClient filesServerApiClient,
        ReplyPreviewResolver replyPreviewResolver, ILogger<ListMessagesCommandHandler> logger)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _federatedReadStatesStorage = federatedReadStatesStorage;
        _filesServerApiClient = filesServerApiClient;
        _replyPreviewResolver = replyPreviewResolver;
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

            var fileIds = messages
                .Where(m => m.Content?.Attachments != null)
                .SelectMany(m => m.Content!.Attachments!)
                // Federated-вложения рендерятся из снапшота (этап 3.1) — файла у нас нет,
                // и спрашивать о нём Files бессмысленно.
                .Where(a => a.OriginServer is null)
                .SelectMany(a =>
                {
                    var ids = new List<string>();
                    if (!string.IsNullOrEmpty(a.FileId)) ids.Add(a.FileId!);
                    if (a.ForwardedAttachments != null)
                        ids.AddRange(a.ForwardedAttachments.Select(fa => fa.FileId).Where(id => !string.IsNullOrEmpty(id)));
                    return ids;
                })
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

            // Объединение с федеративными прочтениями (этап 2.4, docs/rearch/05, «Read receipts»):
            // remote-читатель прочитал сообщение, если оно отправлено не позже, чем выпущена его
            // последняя отметка "прочитано" для этого чата. Клиентский рендер — Фаза 5.
            Dictionary<long, List<Guid>>? federatedReadByMap = null;
            if (messages.Any(m => m.FederatedId.HasValue))
            {
                var readStates = await _federatedReadStatesStorage.GetForChatAsync(request.ChatId);
                if (readStates.Count > 0)
                {
                    federatedReadByMap = messages
                        .Where(m => m.FederatedId.HasValue)
                        .Select(m => new
                        {
                            m.Id,
                            Readers = readStates.Where(s => m.SentAt <= s.ReadAt).Select(s => s.UserUuid).ToList()
                        })
                        .Where(x => x.Readers.Count > 0)
                        .ToDictionary(x => x.Id, x => x.Readers);
                }
            }

            var replyPreviews = await _replyPreviewResolver.ResolveAsync(messages);

            return new ListMessagesResponse()
            {
                Messages =
                {
                    messages.Select(x => x.ToGrpc(filesInfoMap, federatedReadByMap?.GetValueOrDefault(x.Id), replyPreviews))
                }
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