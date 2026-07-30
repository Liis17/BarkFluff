using BarkFluff.Proto.Developers;
using MediatR;

namespace Barkfluff.Developers.Features.GetSectionByKey;

public class GetDocumentationSectionQuery : IRequest<DocumentationSection>
{
    public string Key { get; set; } = string.Empty;
}
