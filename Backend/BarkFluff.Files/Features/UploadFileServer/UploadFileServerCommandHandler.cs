using BarkFluff.Files.Features.UploadFile;
using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UploadFileServer;

/// <summary>
/// Серверная загрузка файла от имени пользователя (сервис Bots).
/// Создаёт запись файла и переиспользует полный пайплайн UploadFileCommand:
/// детекция типа по содержимому, компрессия, превью, дедупликация.
/// </summary>
public class UploadFileServerCommandHandler : IRequestHandler<UploadFileServerCommand, UploadFileServerResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly IMediator _mediator;
    private readonly RunSettings _runSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UploadFileServerCommandHandler> _logger;

    public UploadFileServerCommandHandler(
        UploadedFilesStorage uploadedFilesStorage,
        IMediator mediator,
        RunSettings runSettings,
        IConfiguration configuration,
        ILogger<UploadFileServerCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _mediator = mediator;
        _runSettings = runSettings;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<UploadFileServerResponse> Handle(UploadFileServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Серверная загрузка файла {Filename} ({Size} байт, тип {FileType}) для пользователя {OwnerUserId}",
            request.Filename,
            request.Data.Length,
            request.FileType,
            request.OwnerUserId
        );

        var uploadFile = new Domain.UploadFile
        {
            CreatedAt = DateTime.UtcNow,
            Type = request.FileType,
            Uploaders = new List<long> { request.OwnerUserId },
        };

        var createdFile = await _uploadedFilesStorage.AddToStorage(uploadFile);

        using var uploadCommand = new UploadFileCommand
        {
            FileId = createdFile.Id,
            FileStream = new MemoryStream(request.Data),
            FileName = request.Filename,
            FileSize = request.Data.Length,
        };

        // Пайплайн может вернуть другой id при дедупликации по хешу.
        var finalFileId = Guid.Parse(await _mediator.Send(uploadCommand, cancellationToken));

        var file = await _uploadedFilesStorage.GetFile(finalFileId);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);
        var previewUrl = file?.PreviewId is { } previewId
            ? FileUrlHelper.GenerateDownloadUrl(baseUrl, previewId)
            : string.Empty;

        _logger.LogInformation(
            "Файл {FileId} загружен для пользователя {OwnerUserId}. Размер: {Size} байт",
            finalFileId,
            request.OwnerUserId,
            file?.Size ?? request.Data.Length
        );

        return new UploadFileServerResponse
        {
            FileId = finalFileId.ToString(),
            PreviewUrl = previewUrl,
            FileSize = file?.Size ?? request.Data.Length,
        };
    }
}
