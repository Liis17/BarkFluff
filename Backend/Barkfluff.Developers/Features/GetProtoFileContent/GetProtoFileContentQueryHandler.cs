using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace Barkfluff.Developers.Features.GetProtoFileContent;

public class GetProtoFileContentQueryHandler : IRequestHandler<GetProtoFileContentQuery, GetProtoFileContentResponse>
{
    private readonly ProtoFileProvider _protoProvider;
    private readonly ProtoMetadataStorage _metadataStorage;

    public GetProtoFileContentQueryHandler(ProtoFileProvider protoProvider, ProtoMetadataStorage metadataStorage)
    {
        _protoProvider = protoProvider;
        _metadataStorage = metadataStorage;
    }

    public async Task<GetProtoFileContentResponse> Handle(GetProtoFileContentQuery request, CancellationToken cancellationToken)
    {
        var content = _protoProvider.GetContent(request.FileName)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Proto file '{request.FileName}' not found"));

        var metadata = await _metadataStorage.GetByFileNameAsync(request.FileName);

        var response = new GetProtoFileContentResponse { Content = content };

        if (metadata != null)
        {
            response.Metadata = new ProtoFileInfo
            {
                FileName = metadata.FileName,
                DisplayName = metadata.DisplayName,
                Slug = metadata.Slug,
                Order = metadata.Order,
                RpcDescriptions = metadata.RpcDescriptions
            };
        }

        return response;
    }
}
