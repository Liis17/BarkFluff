using BarkFluff.Files.Mapping;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.GetFilesData;

public class GetFilesDataCommandHandler : IRequestHandler<GetFilesDataCommand, GetFilesDataResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    
    private readonly RunSettings _runSettings;

    public GetFilesDataCommandHandler(UploadedFilesStorage uploadedFilesStorage, RunSettings runSettings)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _runSettings = runSettings;
    }

    public async Task<GetFilesDataResponse> Handle(GetFilesDataCommand request, CancellationToken cancellationToken)
    {
        var files = await _uploadedFilesStorage.GetFiles(request.FileIds);
        
        return new GetFilesDataResponse
        {
            FilesInfos = { files.Select(x => x.ToGrpc(_runSettings)) }
        };
    }
}