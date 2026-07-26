using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ListPinnedMessages;

public class ListPinnedMessagesQueryHandler : IRequestHandler<ListPinnedMessagesQuery, ListPinnedMessagesResponse>
{
    private const int MaxPageSize = 50;

    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UserContext _userContext;
    private readonly ILogger<ListPinnedMessagesQueryHandler> _logger;

    public ListPinnedMessagesQueryHandler(PinnedMessagesStorage pinnedMessagesStorage,
        MessagesStorage messagesStorage, ChatsStorage chatsStorage,
        FilesServerApi.FilesServerApiClient filesServerApiClient, UserContext userContext,
        ILogger<ListPinnedMessagesQueryHandler> logger)
    {
        _pinnedMessagesStorage = pinnedMessagesStorage;
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _filesServerApiClient = filesServerApiClient;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<ListPinnedMessagesResponse> Handle(ListPinnedMessagesQuery request, CancellationToken cancellationToken)
    {
        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var skip = Math.Max(request.Skip, 0);
        var count = request.Count <= 0 ? MaxPageSize : Math.Min(request.Count, MaxPageSize);

        var pins = await _pinnedMessagesStorage.ListByChatAsync(request.ChatId, skip, count);
        var totalCount = await _pinnedMessagesStorage.CountByChatAsync(request.ChatId);

        var response = new ListPinnedMessagesResponse { TotalCount = totalCount };

        if (pins.Count == 0)
        {
            return response;
        }

        var messageIds = pins.Select(p => p.MessageId).ToList();
        var messages = await _messagesStorage.GetMessagesByIdsInChatAsync(request.ChatId, messageIds);
        var messagesById = messages.ToDictionary(m => m.Id);

        var fileIds = new HashSet<string>();
        foreach (var message in messages)
        {
            if (message.Content?.Attachments is null)
            {
                continue;
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
        }

        Dictionary<string, UploadFileInfo>? filesInfoMap = null;

        if (fileIds.Count > 0)
        {
            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest
            {
                FileIds = { fileIds }
            });

            filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);
        }

        foreach (var pin in pins)
        {
            if (!messagesById.TryGetValue(pin.MessageId, out var message))
            {
                _logger.LogDebug(
                    "Закрепление {PinId} ссылается на удалённое/отсутствующее сообщение {MessageId} — пропускаем",
                    pin.Id,
                    pin.MessageId
                );
                continue;
            }

            response.Pinned.Add(pin.ToGrpc(message, filesInfoMap));
        }

        return response;
    }
}
