using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

using System.Text.Json;

using FilesServerApiClient = BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient;
using MessagesServerApiClient = BarkFluff.Proto.Messages.MessagesServerApi.MessagesServerApiClient;

namespace BarkFluff.Users.Features.ExportData;

public class ExportDataCommandHandler : IRequestHandler<ExportDataCommand, ExportDataResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly FilesServerApiClient _filesClient;
    private readonly MessagesServerApiClient _messagesClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ExportDataCommandHandler> _logger;

    public ExportDataCommandHandler(
        UsersStorage usersStorage,
        FilesServerApiClient filesClient,
        MessagesServerApiClient messagesClient,
        MetricsCollector metrics,
        ILogger<ExportDataCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _filesClient = filesClient;
        _messagesClient = messagesClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ExportDataResponse> Handle(ExportDataCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Начало экспорта данных для пользователя {UserId}", request.UserId);

        var response = new ExportDataResponse();

        // 1. Получаем профиль пользователя
        var user = await _usersStorage.GetById(request.UserId);
        if (user == null)
        {
            _logger.LogWarning("Пользователь {UserId} не найден", request.UserId);
            return response;
        }

        var profileJson = JsonSerializer.Serialize(new
        {
            id = user.Id,
            username = user.Username,
            firstName = user.FirstName,
            lastName = user.LastName,
            registrationDate = user.RegistrationDate.ToString("O"),
            profilePicture = user.ProfilePicture,
            bio = user.Bio,
            storageLimitGb = user.StorageLimitGb
        }, new JsonSerializerOptions { WriteIndented = true });

        response.Files.Add(new JsonFile
        {
            Filename = "profile.json",
            Content = profileJson
        });

        // 2. Получаем все сообщения пользователя через MessagesServerApi
        try
        {
            var messagesRequest = new GetUserAllMessagesRequest { UserId = request.UserId };
            var messagesResponse = await _messagesClient.GetUserAllMessagesAsync(
                messagesRequest,
                cancellationToken: cancellationToken);
            _metrics.Increment("messages_fetch_success");

            if (messagesResponse != null)
            {
                // Сериализуем сообщения
                var messagesData = new
                {
                    messages = messagesResponse.Messages.Select(m => new
                    {
                        id = m.Id,
                        chatId = m.ChatId,
                        senderId = m.SenderId,
                        sentAt = m.SentAt.ToString(),
                        text = m.Text,
                        contentType = m.ContentType,
                        readBy = m.ReadBy.ToList(),
                        attachments = m.Attachments.Select(a => new
                        {
                            id = a.Id,
                            type = a.Type,
                            fileId = a.FileId,
                            previewUrl = a.PreviewUrl,
                            fileSize = a.AttachmentSize,
                            previewFileId = a.PreviewFileId,
                            fileName = a.FileName
                        }).ToList()
                    }).ToList(),
                    chats = messagesResponse.Chats.Select(c => new
                    {
                        id = c.Id,
                        title = c.Title,
                        isGroupChat = c.IsGroupChat,
                        memberIds = c.MemberIds.ToList()
                    }).ToList()
                };

                var messagesJson = JsonSerializer.Serialize(
                    messagesData,
                    new JsonSerializerOptions { WriteIndented = true });

                response.Files.Add(new JsonFile
                {
                    Filename = "messages.json",
                    Content = messagesJson
                });

                _logger.LogInformation(
                    "Экспортировано {MessageCount} сообщений и {ChatCount} чатов",
                    messagesResponse.Messages.Count,
                    messagesResponse.Chats.Count);
            }
        }
        catch (Exception ex)
        {
            _metrics.Increment("messages_fetch_errors");
            _logger.LogError(ex, "Ошибка при получении сообщений для пользователя {UserId}", request.UserId);
            // Добавляем информацию об ошибке в экспорт
            response.Files.Add(new JsonFile
            {
                Filename = "messages_error.json",
                Content = JsonSerializer.Serialize(new { error = ex.Message }, new JsonSerializerOptions { WriteIndented = true })
            });
        }

        // 3. Получаем все загруженные файлы пользователя через FilesServerApi
        try
        {
            // Собираем все fileIds из сообщений
            var messagesData = await _messagesClient.GetUserAllMessagesAsync(
                new GetUserAllMessagesRequest { UserId = request.UserId },
                cancellationToken: cancellationToken);
            _metrics.Increment("messages_fetch_success");

            var fileIds = new HashSet<string>();
            if (messagesData != null)
            {
                foreach (var msg in messagesData.Messages)
                {
                    foreach (var attachment in msg.Attachments)
                    {
                        if (!string.IsNullOrEmpty(attachment.FileId))
                        {
                            fileIds.Add(attachment.FileId);
                        }
                        if (!string.IsNullOrEmpty(attachment.PreviewFileId))
                        {
                            fileIds.Add(attachment.PreviewFileId);
                        }
                    }
                }
            }

            // Добавляем аватарку пользователя
            if (!string.IsNullOrEmpty(user.ProfilePicture))
            {
                fileIds.Add(user.ProfilePicture);
            }

            if (!string.IsNullOrEmpty(user.ProfilePicturePreviewUrl))
            {
                fileIds.Add(user.ProfilePicturePreviewUrl);
            }

            if (fileIds.Count > 0)
            {
                var filesRequest = new GetFilesDataRequest();
                filesRequest.FileIds.AddRange(fileIds);

                var filesResponse = await _filesClient.GetFilesDataAsync(
                    filesRequest,
                    cancellationToken: cancellationToken);
                _metrics.Increment("files_fetch_success");

                if (filesResponse != null)
                {
                    var filesData = new
                    {
                        files = filesResponse.FilesInfos.Select(f => new
                        {
                            id = f.Id,
                            fileName = f.FileName,
                            fileSize = f.FileSize,
                            type = f.Type,
                            createdAt = f.CreatedAt.ToString(),
                            uploadedAt = f.UploadedAt.ToString(),
                            etag = f.Etag,
                            fileUrl = f.FileUrl,
                            previewUrl = f.PreviewUrl,
                            previewFileId = f.PreviewFileId,
                            uploaders = f.Uploaders.ToList()
                        }).ToList()
                    };

                    var filesJson = JsonSerializer.Serialize(
                        filesData,
                        new JsonSerializerOptions { WriteIndented = true });

                    response.Files.Add(new JsonFile
                    {
                        Filename = "files.json",
                        Content = filesJson
                    });

                    _logger.LogInformation("Экспортировано {FileCount} файлов", filesResponse.FilesInfos.Count);
                }
            }
            else
            {
                response.Files.Add(new JsonFile
                {
                    Filename = "files.json",
                    Content = JsonSerializer.Serialize(new { files = Array.Empty<object>() }, new JsonSerializerOptions { WriteIndented = true })
                });
            }
        }
        catch (Exception ex)
        {
            _metrics.Increment("files_fetch_errors");
            _logger.LogError(ex, "Ошибка при получении файлов для пользователя {UserId}", request.UserId);
            response.Files.Add(new JsonFile
            {
                Filename = "files_error.json",
                Content = JsonSerializer.Serialize(new { error = ex.Message }, new JsonSerializerOptions { WriteIndented = true })
            });
        }

        _logger.LogInformation(
            "Экспорт данных для пользователя {UserId} завершён. Всего файлов: {FileCount}",
            request.UserId,
            response.Files.Count);

        return response;
    }
}
