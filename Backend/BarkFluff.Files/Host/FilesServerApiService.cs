using BarkFluff.Files.Features.GetFileData;
using BarkFluff.Files.Features.GetFilesData;
using BarkFluff.Files.Features.GetUserStorageInfoServer;
using BarkFluff.Files.Features.UploadBadgeImage;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Files.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class FilesServerApiService : FilesServerApi.FilesServerApiBase
{
    private readonly IMediator _mediator;

    public FilesServerApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<GetFileDataResponse> GetFileData(GetFileDataRequest request, ServerCallContext context)
    {
        var command = new GetFileDataCommand()
        {
            FileId = Guid.Parse(request.FileId)
        };

        return _mediator.Send(command);
    }
    
    public override Task<GetFilesDataResponse> GetFilesData(GetFilesDataRequest request, ServerCallContext context)
    {
        var command = new GetFilesDataCommand()
        {
            FileIds = request.FileIds.Select(Guid.Parse).ToList()
        };

        return _mediator.Send(command);
    }

    public override Task<UploadBadgeImageResponse> UploadBadgeImage(UploadBadgeImageRequest request, ServerCallContext context)
    {
        var command = new UploadBadgeImageCommand
        {
            ImageData = request.ImageData.ToByteArray(),
            Filename = request.Filename
        };

        return _mediator.Send(command);
    }

    public override Task<GetUserStorageInfoResponse> GetUserStorageInfoServer(GetUserStorageInfoServerRequest request, ServerCallContext context)
    {
        var command = new GetUserStorageInfoServerCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }
}