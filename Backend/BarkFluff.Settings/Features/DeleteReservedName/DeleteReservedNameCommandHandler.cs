using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.DeleteReservedName;

public sealed class DeleteReservedNameCommandHandler(SettingsStorage storage)
    : IRequestHandler<DeleteReservedNameCommand, DeleteReservedNameResponse>
{
    public async Task<DeleteReservedNameResponse> Handle(DeleteReservedNameCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteReservedNameAsync(request.Name, cancellationToken);
            return new DeleteReservedNameResponse { Success = true, Message = $"Имя '{request.Name}' удалено из зарезервированных" };
        }
        catch (Exception exception) { return new DeleteReservedNameResponse { Success = false, Message = exception.Message }; }
    }
}
