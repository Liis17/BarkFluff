using Grpc.Core;
using Barkfluff.Developers.Persistence.Services;
using MediatR;

namespace Barkfluff.Developers.Features.DeleteSection;

public class DeleteSectionCommandHandler : IRequestHandler<DeleteSectionCommand, bool>
{
    private readonly DocumentationStorage _storage;

    public DeleteSectionCommandHandler(DocumentationStorage storage)
    {
        _storage = storage;
    }

    public async Task<bool> Handle(DeleteSectionCommand request, CancellationToken cancellationToken)
    {
        var deleted = await _storage.DeleteAsync(request.Key, cancellationToken);
        if (!deleted) throw new RpcException(new Status(StatusCode.NotFound, $"Section '{request.Key}' not found"));
        return true;
    }
}
