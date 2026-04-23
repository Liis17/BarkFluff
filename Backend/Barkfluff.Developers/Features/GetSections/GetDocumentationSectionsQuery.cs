using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace Barkfluff.Developers.Features.GetSections;

public class GetDocumentationSectionsQuery : IRequest<GetDocumentationSectionsResponse> { }

public class GetDocumentationSectionsQueryHandler : IRequestHandler<GetDocumentationSectionsQuery, GetDocumentationSectionsResponse>
{
    private readonly DocumentationStorage _storage;

    public GetDocumentationSectionsQueryHandler(DocumentationStorage storage)
    {
        _storage = storage;
    }

    public async Task<GetDocumentationSectionsResponse> Handle(GetDocumentationSectionsQuery request, CancellationToken cancellationToken)
    {
        var sections = await _storage.GetAllAsync();

        var response = new GetDocumentationSectionsResponse();
        foreach (var s in sections)
        {
            response.Sections.Add(new DocumentationSection
            {
                Key = s.Key,
                Title = s.Title,
                Type = s.Type,
                Order = s.Order,
                Content = s.Content
            });
        }

        return response;
    }
}
