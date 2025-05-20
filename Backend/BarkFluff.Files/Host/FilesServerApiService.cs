using BarkFluff.Files.Features.GetFileData;
using BarkFluff.Files.Features.GetFilesData;
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
}