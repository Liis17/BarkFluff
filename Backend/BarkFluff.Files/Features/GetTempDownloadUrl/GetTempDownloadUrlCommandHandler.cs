using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

        if (files.Count != request.FileIds.Count)
        {
            _logger.LogWarning(
                "Найдено {FoundCount} файлов из {RequestedCount} запрошенных",
                files.Count,
                request.FileIds.Count()
            );

            if (files is null)
            {
                throw new FileNotFoundException();
            }
        }

        var response = new GetTempDownloadUrlResponse()
        {
            FileUrls = { }
        };

        _logger.LogDebug("Создание временных ссылок для {FileCount} файлов", files.Count);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        foreach (var file in files)
        {
            var tempFile = await _tempFilesStorage.CreateTempFile(file.Id);

            var url = FileUrlHelper.GenerateDownloadUrl(baseUrl, tempFile.Id);

            _logger.LogDebug(
                "Создана временная ссылка для файла {FileId}: {TempFileId}",
                file.Id,
                tempFile.Id
            );

            response.FileUrls.Add(new GetTempDownloadUrlResponse.Types.DownloadFileData() { FileId = file.Id.ToString(), Url = url });
        }

        _logger.LogInformation(
            "Создано {UrlCount} временных URL для скачивания файлов",
            response.FileUrls.Count
        );

        return response;
    }
}
