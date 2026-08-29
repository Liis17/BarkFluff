using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace Barkfluff.Developers.Features.GetProtoFileContent;

public class GetProtoFileContentQueryHandler : IRequestHandler<GetProtoFileContentQuery, GetProtoFileContentResponse>
{
    private readonly IPublishedProtoCatalog _catalog;
    private readonly ProtoMetadataStorage _metadataStorage;

    public GetProtoFileContentQueryHandler(
        IPublishedProtoCatalog catalog,
        ProtoMetadataStorage metadataStorage)
    {
        _catalog = catalog;
        _metadataStorage = metadataStorage;
    }

    public async Task<GetProtoFileContentResponse> Handle(GetProtoFileContentQuery request, CancellationToken cancellationToken)
    {
        if (!_catalog.IsPublished(request.FileName))
            throw new RpcException(new Status(StatusCode.NotFound, $"Proto file '{request.FileName}' not found"));

        var metadata = await _metadataStorage.GetByFileNameAsync(request.FileName, cancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Proto file '{request.FileName}' not found"));

        var content = _catalog.GetContent(request.FileName)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Proto file '{request.FileName}' not found"));

        var response = new GetProtoFileContentResponse { Content = content };

        response.Metadata = new ProtoFileInfo
        {
            FileName = metadata.FileName,
            DisplayName = metadata.DisplayName,
            Slug = metadata.Slug,
            Order = metadata.Order,
            RpcDescriptions = metadata.RpcDescriptions
        };

        return response;
    }
}
