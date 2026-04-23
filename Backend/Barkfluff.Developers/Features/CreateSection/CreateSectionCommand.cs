using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Services;
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

public class CreateSectionCommandHandler : IRequestHandler<CreateSectionCommand, DocumentationSection>
{
    private readonly DocumentationStorage _storage;

    public CreateSectionCommandHandler(DocumentationStorage storage)
    {
        _storage = storage;
    }

    public async Task<DocumentationSection> Handle(CreateSectionCommand request, CancellationToken cancellationToken)
    {
        var section = new Domain.DocumentationSection
        {
            Id = Guid.NewGuid(),
            Key = request.Key,
            Title = request.Title,
            Type = request.Type,
            Order = request.Order,
            Content = request.Content
        };

        var created = await _storage.CreateAsync(section);

        return new DocumentationSection
        {
            Key = created.Key,
            Title = created.Title,
            Type = created.Type,
            Order = created.Order,
            Content = created.Content
        };
    }
}
