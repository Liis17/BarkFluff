using MediatR;
using BarkFluff.Proto.Navigator;
using BarkFluff.Navigator.Persistence;
using BarkFluff.Navigator.Domain;
using BarkFluff.Shared.Exceptions.Navigator;

namespace BarkFluff.Navigator.Features.RegisterServer;

public class RegisterServerCommandHandler : IRequestHandler<RegisterServerCommand, RegisterServerResponse>
{
    private readonly ServersStorage _serversStorage;

    public RegisterServerCommandHandler(ServersStorage serversStorage)
    {
        _serversStorage = serversStorage;
    }

    public Task<RegisterServerResponse> Handle(RegisterServerCommand request, CancellationToken cancellationToken)
    {
        var server = request.Server;
        if (string.IsNullOrWhiteSpace(server.BeaconHost))
            throw new BeaconHostEmptyException();
        if (server.BeaconPort == 0)
            throw new BeaconPortEmptyException();
        if (string.IsNullOrWhiteSpace(server.Name))
            throw new NameEmptyException();
        _serversStorage.RegisterServer(server);
        return Task.FromResult(new RegisterServerResponse());
    }
} 