using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Files.Features.GetFilesData;

public class GetFilesDataCommandHandler : IRequestHandler<GetFilesDataCommand, GetFilesDataResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;

    private readonly RunSettings _runSettings;
    private readonly ILogger<GetFilesDataCommandHandler> _logger;

    public GetFilesDataCommandHandler(UploadedFilesStorage uploadedFilesStorage, RunSettings runSettings,
        ILogger<GetFilesDataCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _runSettings = runSettings;
        _logger = logger;
    }

    public async Task<GetFilesDataResponse> Handle(GetFilesDataCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Запрос данных для {FileCount} файлов",
            request.FileIds.Count()
        );

        var files = await _uploadedFilesStorage.GetFiles(request.FileIds);

        _logger.LogInformation(
            "Получены данные для {FoundCount} файлов из {RequestedCount} запрошенных",
            files.Count(),
            request.FileIds.Count()
        );

        return new GetFilesDataResponse
        {
            FilesInfos = { files.Select(x => x.ToGrpc(_runSettings)) }
        };
    }
}