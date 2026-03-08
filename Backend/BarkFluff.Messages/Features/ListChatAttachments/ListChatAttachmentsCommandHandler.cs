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

        // Get attachments from storage
        var (attachments, totalCount) = await _messagesStorage.GetChatAttachmentsAsync(
            request.ChatId,
            request.AttachmentType,
            request.Skip,
            request.Size,
            request.SortDescending);

        // Collect FileIds for batch request to Files service
        var fileIds = attachments
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
                    PreviewFileId = fileInfo?.PreviewFileId ?? string.Empty,
                    FileName = fileInfo?.FileName ?? string.Empty
                }
            });
        }

        return response;
    }
}
