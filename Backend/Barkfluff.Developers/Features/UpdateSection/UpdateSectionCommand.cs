using BarkFluff.Proto.Developers;
using Barkfluff.Developers.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace Barkfluff.Developers.Features.UpdateSection;

public class UpdateSectionCommand : IRequest<DocumentationSection>
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class UpdateSectionCommandHandler : IRequestHandler<UpdateSectionCommand, DocumentationSection>
{
    private readonly DocumentationStorage _storage;

    public UpdateSectionCommandHandler(DocumentationStorage storage)
    {
        _storage = storage;
    }

    public async Task<DocumentationSection> Handle(UpdateSectionCommand request, CancellationToken cancellationToken)
    {
        var updated = await _storage.UpdateAsync(request.Key, request.Title, request.Type, request.Order, request.Content)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Section '{request.Key}' not found"));

        return new DocumentationSection
        {
            Key = updated.Key,
            Title = updated.Title,
            Type = updated.Type,
            Order = updated.Order,
            Content = updated.Content
        };
    }
}
