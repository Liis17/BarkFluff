using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Files.Features.GetTempDownloadUrl;

public class GetTempDownloadUrlCommandHandler : IRequestHandler<GetTempDownloadUrlCommand, GetTempDownloadUrlResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly TempFilesStorage _tempFilesStorage;
    private readonly MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly RunSettings _runSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetTempDownloadUrlCommandHandler> _logger;

    public GetTempDownloadUrlCommandHandler(UploadedFilesStorage uploadedFilesStorage, TempFilesStorage tempFilesStorage,
        MessagesServerApi.MessagesServerApiClient messagesClient,
        RunSettings runSettings, IConfiguration configuration, ILogger<GetTempDownloadUrlCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _tempFilesStorage = tempFilesStorage;
        _messagesClient = messagesClient;
        _runSettings = runSettings;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GetTempDownloadUrlResponse> Handle(GetTempDownloadUrlCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Запрос временных URL для скачивания {FileCount} файлов",
            request.FileIds.Count()
        );

        var files = await _uploadedFilesStorage.GetFiles(request.FileIds);
        
        if (files is null)
        {
            throw new FileNotFoundException();
        }
        
        if (files.Count != request.FileIds.Count)
        {
            _logger.LogWarning(
                "Найдено {FoundCount} файлов из {RequestedCount} запрошенных",
                files.Count,
                request.FileIds.Count()
            );
        }

        var response = new GetTempDownloadUrlResponse()
        {
            FileUrls = { }
        };

        _logger.LogDebug("Создание временных ссылок для {FileCount} файлов", files.Count);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        // Один INSERT на все TempFile вместо N round-trip'ов к БД
        var tempFiles = await _tempFilesStorage.CreateTempFilesBatchAsync(
            files.Select(f => f.Id), cancellationToken);

        foreach (var tempFile in tempFiles)
        {
            var url = FileUrlHelper.GenerateDownloadUrl(baseUrl, tempFile.Id);

            _logger.LogDebug(
                "Создана временная ссылка для файла {FileId}: {MaskedToken}",
                tempFile.OriginalFileId,
                FileUrlHelper.MaskCapabilityToken(tempFile.Id)
            );

            response.FileUrls.Add(new GetTempDownloadUrlResponse.Types.DownloadFileData()
            {
                FileId = tempFile.OriginalFileId.ToString(),
                Url = url
            });
        }

        await AddFederatedUrlsAsync(request, response, baseUrl, cancellationToken);

        _logger.LogInformation(
            "Создано {UrlCount} временных URL для скачивания файлов",
            response.FileUrls.Count
        );

        return response;
    }

    /// <summary>
    /// Capability-ссылки на federated-вложения (этап 3.3). Второй, независимый от origin,
    /// уровень доступа: origin решает «этой ноде можно», мы — «этому пользователю можно».
    /// </summary>
    /// <remarks>
    /// Недоступное вложение просто не попадает в ответ — ровно та же семантика, что у
    /// ненайденного локального file_id. Отдельной ошибки нет намеренно: иначе перебором
    /// (origin, file_id) можно было бы выяснять, что существует на чужих нодах.
    /// </remarks>
    private async Task AddFederatedUrlsAsync(
        GetTempDownloadUrlCommand request,
        GetTempDownloadUrlResponse response,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        if (request.FedFiles.Count == 0)
        {
            return;
        }

        var granted = new List<TempFile>();

        foreach (var fedFile in request.FedFiles)
        {
            var access = await _messagesClient.CheckFedFileUserAccessAsync(
                new CheckFedFileUserAccessRequest
                {
                    UserId = request.RequesterUserId,
                    OriginServer = fedFile.OriginServer,
                    FileId = fedFile.FileId,
                },
                cancellationToken: cancellationToken);

            if (!access.Allowed || !Guid.TryParse(fedFile.FileId, out var originalFileId))
            {
                _logger.LogDebug(
                    "Доступ к federated-файлу {FileId} с ноды {Origin} для пользователя {UserId} не подтверждён",
                    fedFile.FileId, fedFile.OriginServer, request.RequesterUserId);
                continue;
            }

            granted.Add(new TempFile
            {
                OriginalFileId = originalFileId,
                OriginServer = fedFile.OriginServer,
                FileName = string.IsNullOrEmpty(access.FileName) ? null : access.FileName,
                SizeBytes = access.SizeBytes,
                AttachmentType = access.AttachmentType,
            });
        }

        var tempFiles = await _tempFilesStorage.CreateFederatedTempFilesBatchAsync(granted, cancellationToken);

        foreach (var tempFile in tempFiles)
        {
            response.FileUrls.Add(new GetTempDownloadUrlResponse.Types.DownloadFileData
            {
                FileId = tempFile.OriginalFileId.ToString(),
                Url = FileUrlHelper.GenerateDownloadUrl(baseUrl, tempFile.Id),
            });
        }
    }
}
