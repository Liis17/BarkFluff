using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Services;
using MediatR;

namespace Barkfluff.Developers.Features.CreateSection;

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

        var created = await _storage.CreateAsync(section, cancellationToken);

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
