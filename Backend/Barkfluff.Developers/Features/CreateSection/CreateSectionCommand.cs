using BarkFluff.Proto.Developers;
using MediatR;

namespace Barkfluff.Developers.Features.CreateSection;

public class CreateSectionCommand : IRequest<DocumentationSection>
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Content { get; set; } = string.Empty;
}
