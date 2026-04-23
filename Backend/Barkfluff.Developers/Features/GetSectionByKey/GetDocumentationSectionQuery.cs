using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace Barkfluff.Developers.Features.GetSectionByKey;

public class GetDocumentationSectionQuery : IRequest<DocumentationSection>
{
    public string Key { get; set; } = string.Empty;
}

public class GetDocumentationSectionQueryHandler : IRequestHandler<GetDocumentationSectionQuery, DocumentationSection>
{
    private readonly DocumentationStorage _storage;

    public GetDocumentationSectionQueryHandler(DocumentationStorage storage)
    {
        _storage = storage;
    }

    public async Task<DocumentationSection> Handle(GetDocumentationSectionQuery request, CancellationToken cancellationToken)
    {
        var section = await _storage.GetByKeyAsync(request.Key)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Section '{request.Key}' not found"));

        return new DocumentationSection
        {
            Key = section.Key,
            Title = section.Title,
            Type = section.Type,
            Order = section.Order,
            Content = section.Content
        };
    }
}
