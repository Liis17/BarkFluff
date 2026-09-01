using BarkFluff.Proto.Configuration;
using BarkFluff.Settings.Persistence.Services;
using MediatR;

namespace BarkFluff.Settings.Features.AddReservedName;

public sealed class AddReservedNameCommandHandler(SettingsStorage storage)
    : IRequestHandler<AddReservedNameCommand, AddReservedNameResponse>
{
    public async Task<AddReservedNameResponse> Handle(AddReservedNameCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await storage.AddReservedNameAsync(request.Name, cancellationToken);
            return new AddReservedNameResponse { Success = true, Message = $"Имя '{request.Name}' добавлено в зарезервированные" };
        }
        catch (Exception exception) { return new AddReservedNameResponse { Success = false, Message = exception.Message }; }
    }
}
