using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

using Message = BarkFluff.Messages.Domain.Message;
using MessageAttachment = BarkFluff.Messages.Domain.MessageAttachment;

namespace BarkFluff.Messages.Features.EditMessage;

using BarkFluff.Messages.Domain;

public class EditMessageCommandHandler : IRequestHandler<EditMessageCommand, EditMessageResponse>
{
    private const int MaxTextLength = 4096;
    private const int MaxAttachmentsPerMessage = 10;

    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<EditMessageCommandHandler> _logger;

    private readonly Dictionary<UploadFileType, Domain.MessageAttachmentType> _attachmentMap =
        new()
        {
            { UploadFileType.MessageAttachmentImage, Domain.MessageAttachmentType.Image },
            { UploadFileType.MessageAttachmentDocument, Domain.MessageAttachmentType.Document },
            { UploadFileType.MessageAttachmentGif, Domain.MessageAttachmentType.Gif },
            { UploadFileType.MessageAttachmentVideo, Domain.MessageAttachmentType.Video },
            { UploadFileType.MessageAttachmentAudio, Domain.MessageAttachmentType.Audio },
            { UploadFileType.MessageAttachmentVoice, Domain.MessageAttachmentType.Voice },
            { UploadFileType.MessageAttachmentSticker, Domain.MessageAttachmentType.Sticker }
        };

    public EditMessageCommandHandler(MessagesStorage messagesStorage, ChatsStorage chatsStorage,
        FilesServerApi.FilesServerApiClient filesServerApiClient, UserContext userContext,
        MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<EditMessageCommandHandler> logger)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _filesServerApiClient = filesServerApiClient;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<EditMessageResponse> Handle(EditMessageCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Редактирование сообщения {MessageId} пользователем {UserId}",
            request.MessageId,
            _userContext.UserId
        );

        var message = await _messagesStorage.GetMessageById(request.MessageId);

        if (message is null)
        {
            _logger.LogWarning(
                "Сообщение {MessageId} не найдено для редактирования пользователем {UserId}",
                request.MessageId,
                _userContext.UserId
            );
            throw new MessageNotFoundException();
        }

        if (message.SenderId != _userContext.UserId)
        {
            _logger.LogWarning(
                "Пользователь {UserId} попытался отредактировать чужое сообщение {MessageId} (автор {SenderId})",
                _userContext.UserId,
                request.MessageId,
                message.SenderId
            );
            throw new NoPermissionException();
        }

        if (message.Type == Domain.MessageContentType.System)
        {
            _logger.LogWarning(
                "Пользователь {UserId} попытался отредактировать системное сообщение {MessageId}",
                _userContext.UserId,
                request.MessageId
            );
            throw new NoPermissionException();
        }

        if (message.IsDeleted)
        {
            _logger.LogWarning(
                "Сообщение {MessageId} удалено и не может быть отредактировано",
                request.MessageId
            );
            throw new MessageNotFoundException();
        }

        var hasText = !string.IsNullOrEmpty(request.Text);
        var hasFiles = request.FileIds is { Count: > 0 };

        if (!hasText && !hasFiles)
        {
            _logger.LogWarning(
                "Попытка отредактировать сообщение {MessageId} без текста и вложений",
                request.MessageId
            );
            throw new MessageNotContainContextException();
        }

        if (request.Text is { Length: > MaxTextLength })
        {
            _logger.LogWarning(
                "Новый текст сообщения {MessageId} превышает лимит {MaxTextLength} символов: {ActualLength}",
                request.MessageId,
                MaxTextLength,
                request.Text.Length
            );
            throw new MessageTextTooLongException();
        }

        if (hasFiles && request.FileIds!.Count > MaxAttachmentsPerMessage)
        {
            _logger.LogWarning(
                "Сообщение {MessageId} содержит слишком много вложений: {Count}",
                request.MessageId,
                request.FileIds.Count
            );
            throw new TooManyAttachmentsException();
        }

        var filesInfoMap = new Dictionary<string, UploadFileInfo>();

        var newAttachments = new List<MessageAttachment>();

        if (hasFiles)
        {
            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest
            {
                FileIds = { request.FileIds!.Select(x => x.ToString()) }
            });

            if (filesInfo.FilesInfos.Any(x => !_attachmentMap.ContainsKey(x.Type)))
            {
                _logger.LogWarning(
                    "В сообщении {MessageId} обнаружены неподдерживаемые типы файлов",
                    request.MessageId
                );
                throw new FileNotSupportedException();
            }

            filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);

            newAttachments = filesInfo.FilesInfos.Select(x => new MessageAttachment
            {
                FileId = x.Id,
                FileSize = x.FileSize,
                PreviewUrl = x.PreviewUrl,
                Type = _attachmentMap[x.Type]
            }).ToList();
        }

        message.Content ??= new Domain.MessageContent();
        var existingAttachments = message.Content.Attachments ?? new List<MessageAttachment>();

        // Сохраняем forwarded-вложения (forward-снапшот не редактируется)
        var forwardedAttachments = existingAttachments
            .Where(a => a.Type == Domain.MessageAttachmentType.ForwardedMessage)
            .ToList();

        // Очищаем коллекцию in-place — EF корректно сделает delete+insert
        existingAttachments.Clear();

        foreach (var fwd in forwardedAttachments)
        {
            existingAttachments.Add(fwd);

            // Восстанавливаем filesInfoMap для вложений пересланного сообщения
            var forwardedFileIds = fwd.ForwardedAttachments?
                .Where(fa => !string.IsNullOrEmpty(fa.FileId))
                .Select(fa => fa.FileId)
                .ToList();

            if (forwardedFileIds is { Count: > 0 })
            {
                var fwdInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest
                {
                    FileIds = { forwardedFileIds }
                });

                foreach (var fi in fwdInfo.FilesInfos)
                {
                    filesInfoMap.TryAdd(fi.Id, fi);
                }
            }
        }

        foreach (var att in newAttachments)
        {
            existingAttachments.Add(att);
        }

        message.Content.Attachments = existingAttachments;
        message.Content.Text = request.Text;
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;
        message.LastChangeAt = message.EditedAt.Value;

        await _messagesStorage.SaveChangesAsync();

        _logger.LogInformation(
            "Сообщение {MessageId} отредактировано пользователем {UserId}",
            request.MessageId,
            _userContext.UserId
        );

        var members = await _chatsStorage.GetChatMembers(message.ChatId, 0, int.MaxValue);
        var memberIds = members.LocalUserIds();

        await _messageQueueSender.SendEdited(message, message.ChatId, memberIds, filesInfoMap);

        _metrics.Increment("messages_edited");
        if (hasText)
        {
            _metrics.Increment("messages_edited_with_text");
        }

        if (newAttachments.Count > 0)
        {
            _metrics.Increment("messages_edited_with_attachments");
        }

        return new EditMessageResponse { Message = message.ToGrpc(filesInfoMap) };
    }
}
