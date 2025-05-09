using BarkFluff.Files.Features.GetFileData;
using BarkFluff.Proto.Files;
using Grpc.Core;
using MediatR;

namespace BarkFluff.Files.Host;

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
}