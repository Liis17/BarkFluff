using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Files;

using MediatR;


namespace BarkFluff.Files.Features.GetUploadUrl;

public class GetUploadUrlCommandHandler : IRequestHandler<GetUploadUrlCommand, GetUploadUrlResponse>
{

    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly RunSettings _runSettings;
    private readonly IConfiguration _configuration;
    private readonly UserContext _userContext;
    private readonly ILogger<GetUploadUrlCommandHandler> _logger;


    public GetUploadUrlCommandHandler(UploadedFilesStorage uploadedFilesStorage, UserContext userContext,
        RunSettings runSettings, IConfiguration configuration,
        ILogger<GetUploadUrlCommandHandler> logger)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _userContext = userContext;
        _runSettings = runSettings;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GetUploadUrlResponse> Handle(GetUploadUrlCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Запрос URL для загрузки файла. Тип: {FileType}, UserId: {UserId}",
            request.Type,
            _userContext.UserId
        );

        var uploadFile = new Domain.UploadFile()
        {
            CreatedAt = DateTime.UtcNow,
            Type = request.Type,
            Uploaders = new List<long> { _userContext.UserId },
        };

        var file = await _uploadedFilesStorage.AddToStorage(uploadFile);

        var baseUrl = FileUrlHelper.GetPublicBaseUrl(_configuration, _runSettings);
        var uploadUrl = FileUrlHelper.GenerateUploadUrl(baseUrl, file.Id);

        _logger.LogInformation(
            "URL для загрузки создан. FileId: {FileId}, Тип: {FileType}, URL: {UploadUrl}",
            file.Id,
            request.Type,
            uploadUrl
        );

        return new GetUploadUrlResponse()
        {
            Url = uploadUrl,
            FileId = file.Id.ToString()
        };
    }
}