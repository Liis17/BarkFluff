using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetTempDownloadUrl;

public class GetTempDownloadUrlCommandHandler : IRequestHandler<GetTempDownloadUrlCommand, GetTempDownloadUrlResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly TempFilesStorage _tempFilesStorage;
    private readonly RunSettings _runSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetTempDownloadUrlCommandHandler> _logger;

    public GetTempDownloadUrlCommandHandler(UploadedFilesStorage uploadedFilesStorage, TempFilesStorage tempFilesStorage,
        RunSettings runSettings, IConfiguration configuration, ILogger<GetTempDownloadUrlCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _tempFilesStorage = tempFilesStorage;
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
                "Создана временная ссылка для файла {FileId}: {TempFileId}",
                tempFile.OriginalFileId,
                tempFile.Id
            );

            response.FileUrls.Add(new GetTempDownloadUrlResponse.Types.DownloadFileData()
            {
                FileId = tempFile.OriginalFileId.ToString(),
                Url = url
            });
        }

        _logger.LogInformation(
            "Создано {UrlCount} временных URL для скачивания файлов",
            response.FileUrls.Count
        );

        return response;
    }
}
