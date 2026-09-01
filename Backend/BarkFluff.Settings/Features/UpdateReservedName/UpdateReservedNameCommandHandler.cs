using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.UpdateReservedName;

public sealed class UpdateReservedNameCommandHandler(SettingsStorage storage)
    : IRequestHandler<UpdateReservedNameCommand, UpdateReservedNameResponse>
{
    public async Task<UpdateReservedNameResponse> Handle(UpdateReservedNameCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await storage.UpdateReservedNameAsync(request.OldName, request.NewName, cancellationToken);
            return new UpdateReservedNameResponse { Success = true, Message = $"Имя '{request.OldName}' переименовано в '{request.NewName}'" };
        }
        catch (Exception exception) { return new UpdateReservedNameResponse { Success = false, Message = exception.Message }; }
    }
}
