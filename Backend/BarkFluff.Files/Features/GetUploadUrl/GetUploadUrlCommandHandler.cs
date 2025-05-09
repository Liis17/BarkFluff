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

    private readonly UserContext _userContext;

    public GetUploadUrlCommandHandler(UploadedFilesStorage uploadedFilesStorage, UserContext userContext, RunSettings runSettings)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _userContext = userContext;
        _runSettings = runSettings;
    }

    public async Task<GetUploadUrlResponse> Handle(GetUploadUrlCommand request, CancellationToken cancellationToken)
    {
        var uploadFile = new Domain.UploadFile()
        {
            CreatedAt = DateTime.UtcNow,
            Type = request.Type,
            Uploader = _userContext.UserId
        };
        
        var file = await _uploadedFilesStorage.AddToStorage(uploadFile);

        return new GetUploadUrlResponse()
        {
            Url = $"{_runSettings.Host}:{_runSettings.Http1Port}/upload/{file.Id}"
        };
    }
}