using BarkFluff.Files.Helpers;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.GetTempDownloadUrl;

public class GetTempDownloadUrlCommandHandler : IRequestHandler<GetTempDownloadUrlCommand, GetTempDownloadUrlResponse>
{
    private readonly UploadedFilesStorage _uploadedFilesStorage;
    private readonly TempFilesStorage _tempFilesStorage;
    private readonly RunSettings _runSettings;

    public GetTempDownloadUrlCommandHandler(UploadedFilesStorage uploadedFilesStorage, TempFilesStorage tempFilesStorage, 
        RunSettings runSettings)
    {
        _uploadedFilesStorage = uploadedFilesStorage;
        _tempFilesStorage = tempFilesStorage;
        _runSettings = runSettings;
    }

    public async Task<GetTempDownloadUrlResponse> Handle(GetTempDownloadUrlCommand request, CancellationToken cancellationToken)
    {
        var files = await _uploadedFilesStorage.GetFiles(request.FileIds);

        if (files.Count != request.FileIds.Count)
        {
            if (files is null)
            {
                throw new FileNotFoundException();
            }
        }
        
        var response = new GetTempDownloadUrlResponse()
        {
            FileUrls = {  }
        };
        
        foreach (var file in files)
        {
            var tempFile = await _tempFilesStorage.CreateTempFile(file.Id);
        
            var url = FileUrlHelper.GenerateDownloadUrl(_runSettings.Host, _runSettings.Http1Port, tempFile.Id);
            
            response.FileUrls.Add(new GetTempDownloadUrlResponse.Types.DownloadFileData() { FileId = file.Id.ToString(), Url = url });
        }
        
        return response;
    }
}