using BarkFluff.Files.Helpers;
using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.GetFileData;

public class GetFileDataCommandHandler : IRequestHandler<GetFileDataCommand, GetFileDataResponse>
{

    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly RunSettings _runSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetFileDataCommandHandler> _logger;

    public GetFileDataCommandHandler(UploadedFilesStorage uploadedFilesStorage, RunSettings runSettings,
        IConfiguration configuration, ILogger<GetFileDataCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _runSettings = runSettings;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GetFileDataResponse> Handle(GetFileDataCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос данных файла {FileId}", request.FileId);

        var file = await _uploadedFilesStorage.GetFile(request.FileId);

        if (file is null)
        {
            _logger.LogWarning("Файл {FileId} не найден", request.FileId);
            throw new FileNotFoundException();
        }

        _logger.LogInformation(
            "Данные файла {FileId} получены. Имя: {FileName}, Тип: {FileType}, Размер: {FileSize} байт",
            request.FileId,
            file.Filename,
            file.Type,
            file.Size
        );

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);

        return new GetFileDataResponse()
        {
            FileInfo = file.ToGrpc(baseUrl)
        };
    }
}
