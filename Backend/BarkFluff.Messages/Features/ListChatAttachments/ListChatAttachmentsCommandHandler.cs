using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Exceptions.Messages;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Messages.Features.ListChatAttachments;

public class ListChatAttachmentsCommandHandler : IRequestHandler<ListChatAttachmentsCommand, ListChatAttachmentsResponse>
{
    private const int FileNameHydrationBatchSize = 50;
    private readonly UserContext _userContext;
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly ILogger<ListChatAttachmentsCommandHandler> _logger;

    public ListChatAttachmentsCommandHandler(
        UserContext userContext,
        ChatsStorage chatsStorage,
        MessagesStorage messagesStorage,
        FilesServerApi.FilesServerApiClient filesServerApiClient,
        ILogger<ListChatAttachmentsCommandHandler> logger)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _filesServerApiClient = filesServerApiClient;
        _logger = logger;
    }

    public async Task<ListChatAttachmentsResponse> Handle(ListChatAttachmentsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Получение вложений чата {ChatId} для пользователя {UserId}. Skip: {Skip}, Size: {Size}",
            request.ChatId,
            _userContext.UserId,
            request.Skip,
            request.Size
        );

        // Check access to chat
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

        var fileNameQuery = request.FileNameQuery?.Trim();
        if (!string.IsNullOrEmpty(fileNameQuery))
        {
            // Поиск имён определён только для документов. Явный фильтр другого
            // типа не подменяем документами: такая комбинация не имеет совпадений.
            if (request.AttachmentType.HasValue
                && request.AttachmentType.Value != Domain.MessageAttachmentType.Unknown
                && request.AttachmentType.Value != Domain.MessageAttachmentType.Document)
            {
                return new ListChatAttachmentsResponse();
            }

            request.AttachmentType = Domain.MessageAttachmentType.Document;
            await HydrateLegacyDocumentFileNames(request.ChatId, cancellationToken);
        }

        // Get attachments from storage
        var (attachments, totalCount) = await _messagesStorage.GetChatAttachmentsAsync(
            request.ChatId,
            request.AttachmentType,
            request.Skip,
            request.Size,
            request.SortDescending,
            fileNameQuery);

        // Collect FileIds for batch request to Files service
        var fileIds = attachments
            // Federated-вложения рендерятся из снапшота (этап 3.1) — Files не дёргаем.
            .Where(a => a.OriginServer is null)
            .Select(a => a.FileId)
            .Distinct()
            .ToList();

        Dictionary<string, UploadFileInfo> filesInfoMap = new();
        if (fileIds.Any())
        {
            _logger.LogDebug("Получение информации о {FileCount} файлах", fileIds.Count);
            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest { FileIds = { fileIds } });
            filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);
        }

        _logger.LogInformation(
            "Получено {AttachmentCount} вложений из {TotalCount} для чата {ChatId}",
            attachments.Count,
            totalCount,
            request.ChatId
        );

        // Map results to response
        var response = new ListChatAttachmentsResponse
        {
            TotalCount = totalCount
        };

        foreach (var attachment in attachments)
        {
            var fileInfo = filesInfoMap.GetValueOrDefault(attachment.FileId);

            response.Attachments.Add(new ChatAttachmentInfo
            {
                MessageId = attachment.MessageId,
                AttachmentId = attachment.AttachmentId,
                SentAt = Timestamp.FromDateTime(attachment.SentAt),
                SenderId = attachment.SenderId,
                Attachment = new MessageAttachment
                {
                    Id = attachment.AttachmentId,
                    Type = (MessageAttachmentType)(int)attachment.AttachmentType,
                    FileId = attachment.FileId,
                    PreviewUrl = attachment.PreviewUrl ?? string.Empty,
                    AttachmentSize = attachment.FileSize,
                    OriginServer = attachment.OriginServer ?? string.Empty,
                    // Для federated-вложения метаданные из снапшота, для локального — из Files.
                    PreviewFileId = attachment.OriginServer is null
                        ? fileInfo?.PreviewFileId ?? string.Empty
                        : attachment.PreviewFileId ?? string.Empty,
                    FileName = attachment.OriginServer is null
                        ? fileInfo?.FileName ?? string.Empty
                        : attachment.FileName ?? string.Empty,
                    ImageWidth = attachment.ImageWidth ?? 0,
                    ImageHeight = attachment.ImageHeight ?? 0,
                }
            });
        }

        return response;
    }

    private async Task HydrateLegacyDocumentFileNames(Guid chatId, CancellationToken cancellationToken)
    {
        var fileIds = await _messagesStorage.GetDocumentFileIdsMissingNamesAsync(chatId);
        if (fileIds.Count == 0)
            return;

        var names = new Dictionary<string, string>(fileIds.Count);
        foreach (var batch in fileIds.Chunk(FileNameHydrationBatchSize))
        {
            var validIds = batch.Where(id => Guid.TryParse(id, out _)).ToList();
            foreach (var fileId in batch)
                names[fileId] = string.Empty;

            if (validIds.Count == 0)
                continue;

            var files = await _filesServerApiClient.GetFilesDataAsync(
                new GetFilesDataRequest { FileIds = { validIds } },
                cancellationToken: cancellationToken);

            foreach (var file in files.FilesInfos)
                names[file.Id] = file.FileName;
        }

        await _messagesStorage.SetDocumentFileNamesAsync(names);
    }
}
