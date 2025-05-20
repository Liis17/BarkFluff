using BarkFluff.Files.Mapping;
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
            FileInfo = file.ToGrpc(_runSettings)
        };
    }
}