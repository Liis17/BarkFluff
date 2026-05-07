using BarkFluff.Messages.Persistence;
using BarkFluff.Proto.Messages;

using Google.Protobuf.WellKnownTypes;

using MediatR;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Features.ExportData;

public class GetUserAllMessagesQueryHandler : IRequestHandler<GetUserAllMessagesQuery, GetUserAllMessagesResponse>
{
    private readonly MessagesContext _context;
    private readonly ILogger<GetUserAllMessagesQueryHandler> _logger;

    public GetUserAllMessagesQueryHandler(
        MessagesContext context,
        ILogger<GetUserAllMessagesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<GetUserAllMessagesResponse> Handle(GetUserAllMessagesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Экспорт всех сообщений для пользователя {UserId}", request.UserId);

        // Получаем все чаты, где пользователь является участником
        var userChatIds = await _context.ChatMembers
            .Where(cm => cm.UserId == request.UserId)
            .Select(cm => cm.ChatId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var response = new GetUserAllMessagesResponse();

        // Стримим сообщения построчно через серверный курсор — без загрузки всей истории в память.
        // Каждое сообщение конвертируется в ExportMessage и оригинальный объект освобождается.
        var messagesStream = _context.Messages
            .AsNoTracking()
            .Where(m => userChatIds.Contains(m.ChatId))
            .OrderBy(m => m.SentAt)
            .AsAsyncEnumerable();

        await foreach (var message in messagesStream.WithCancellation(cancellationToken))
        {
            var exportMessage = new ExportMessage
            {
                Id = message.Id,
                ChatId = message.ChatId.ToString(),
                SenderId = message.SenderId,
                SentAt = Timestamp.FromDateTime(message.SentAt),
                Text = message.Content?.Text ?? string.Empty,
                ContentType = (int)message.Type
            };

            if (message.ReadBy != null)
            {
                exportMessage.ReadBy.AddRange(message.ReadBy);
            }

            if (message.Content?.Attachments != null)
            {
                foreach (var attachment in message.Content.Attachments)
                {
                    var exportAttachment = new ExportAttachment
                    {
                        Id = attachment.Id,
                        Type = (int)attachment.Type,
                        FileId = attachment.FileId,
                        PreviewUrl = attachment.PreviewUrl ?? string.Empty,
                        AttachmentSize = attachment.FileSize,
                        PreviewFileId = string.Empty,
                        FileName = string.Empty
                    };
                    exportMessage.Attachments.Add(exportAttachment);
                }
            }

            response.Messages.Add(exportMessage);
        }

        // Получаем информацию о чатах
        var chats = await _context.Chats
            .Where(c => userChatIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        // Получаем участников чатов
        var allChatMembers = await _context.ChatMembers
            .Where(cm => userChatIds.Contains(cm.ChatId))
            .ToListAsync(cancellationToken);

        // Конвертируем чаты
        foreach (var chat in chats)
        {
            var exportChat = new ExportChat
            {
                Id = chat.Id.ToString(),
                Title = chat.Title ?? string.Empty,
                IsGroupChat = chat.IsGroupChat
            };

            // Добавляем участников чата
            var members = allChatMembers
                .Where(cm => cm.ChatId == chat.Id)
                .Select(cm => cm.UserId)
                .Distinct();

            exportChat.MemberIds.AddRange(members);

            response.Chats.Add(exportChat);
        }

        _logger.LogInformation(
            "Экспортировано {MessageCount} сообщений и {ChatCount} чатов для пользователя {UserId}",
            response.Messages.Count,
            response.Chats.Count,
            request.UserId);

        return response;
    }
}
