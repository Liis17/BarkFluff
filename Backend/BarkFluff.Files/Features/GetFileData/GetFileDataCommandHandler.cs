using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Files.Features.GetFileData;

public class GetFileDataCommandHandler : IRequestHandler<GetFileDataCommand, GetFileDataResponse>
{
    
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly RunSettings _runSettings;

    public GetFileDataCommandHandler(UploadedFilesStorage uploadedFilesStorage, RunSettings runSettings)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _runSettings = runSettings;
    }

    public async Task<GetFileDataResponse> Handle(GetFileDataCommand request, CancellationToken cancellationToken)
    {
        var file = await _uploadedFilesStorage.GetFile(request.FileId);

        if (file is null)
        {
            throw new FileNotFoundException();
        }

        return new GetFileDataResponse()
        {
            FileInfo = new UploadFileInfo()
            {
                CreatedAt = Timestamp.FromDateTime(file.CreatedAt),
                Etag = file.Etag ?? string.Empty,
                FileName = file.Filename ?? string.Empty,
                Id = file.Id.ToString(),
                Type = (UploadFileType)(int)file.Type,
                UploadedAt = Timestamp.FromDateTime(file.UploadedAt ?? DateTime.MinValue), Uploader = file.Uploader,
                FileUrl = $"{_runSettings.Host}:{_runSettings.Http1Port}/download/{file.Id}"
            }
        };
    }
}