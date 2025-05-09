using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;
using Google.Protobuf.WellKnownTypes;
using MediatR;

namespace BarkFluff.Files.Features.GetFileData;

public class GetFileDataCommandHandler : IRequestHandler<GetFileDataCommand, GetFileDataResponse>
{
    
    private readonly UploadedFilesStorage _uploadedFilesStorage;

    public GetFileDataCommandHandler(UploadedFilesStorage uploadedFilesStorage)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
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
                UploadedAt = Timestamp.FromDateTime(file.UploadedAt ?? DateTime.MinValue), Uploader = file.Uploader
            }
        };
    }
}