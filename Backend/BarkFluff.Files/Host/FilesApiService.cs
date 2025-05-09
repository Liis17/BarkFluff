using BarkFluff.Files.Features.GetUploadUrl;
using BarkFluff.Proto.Files;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class FilesApiService : FilesApi.FilesApiBase
{
    private readonly IMediator _mediator;

    public FilesApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<GetUploadUrlResponse> GetUploadUrl(GetUploadUrlRequest request, ServerCallContext context)
    {
        var command = new GetUploadUrlCommand()
        {
            Type = (UploadFileType)(int)request.FileType
        };
        
        return _mediator.Send(command);
    }
}