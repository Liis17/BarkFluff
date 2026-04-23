using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Services;
using MediatR;

namespace Barkfluff.Developers.Features.GetProtoFiles;

public class GetProtoFilesQuery : IRequest<GetProtoFilesResponse> { }

public class GetProtoFilesQueryHandler : IRequestHandler<GetProtoFilesQuery, GetProtoFilesResponse>
{
    private readonly ProtoMetadataStorage _storage;

    public GetProtoFilesQueryHandler(ProtoMetadataStorage storage)
    {
        _storage = storage;
    }

    public async Task<GetProtoFilesResponse> Handle(GetProtoFilesQuery request, CancellationToken cancellationToken)
    {
        var files = await _storage.GetAllAsync();

        var response = new GetProtoFilesResponse();
        foreach (var f in files)
        {
            response.Files.Add(new ProtoFileInfo
            {
                FileName = f.FileName,
                DisplayName = f.DisplayName,
                Slug = f.Slug,
                Order = f.Order,
                RpcDescriptions = f.RpcDescriptions
            });
        }

        return response;
    }
}
