using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Services;
using MediatR;

namespace Barkfluff.Developers.Features.GetProtoFiles;

public class GetProtoFilesQueryHandler : IRequestHandler<GetProtoFilesQuery, GetProtoFilesResponse>
{
    private readonly ProtoMetadataStorage _storage;
    private readonly IPublishedProtoCatalog _catalog;

    public GetProtoFilesQueryHandler(ProtoMetadataStorage storage, IPublishedProtoCatalog catalog)
    {
        _storage = storage;
        _catalog = catalog;
    }

    public async Task<GetProtoFilesResponse> Handle(GetProtoFilesQuery request, CancellationToken cancellationToken)
    {
        var files = (await _storage.GetAllAsync(cancellationToken))
            .Where(file => _catalog.IsPublished(file.FileName) && _catalog.GetContent(file.FileName) is not null)
            .OrderBy(file => file.Order)
            .ThenBy(file => file.FileName, StringComparer.Ordinal);

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
