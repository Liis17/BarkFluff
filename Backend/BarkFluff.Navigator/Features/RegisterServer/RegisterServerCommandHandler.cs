using BarkFluff.Navigator.Persistence;
using BarkFluff.Proto.Navigator;
using BarkFluff.Shared.Exceptions.Navigator;

using MediatR;

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

        if (server.BeaconPort <= 0 || server.BeaconPort > 65535)
            throw new ArgumentException("ѕорт должен быть в диапазоне от 1 до 65535");

        if (string.IsNullOrWhiteSpace(server.Name))
            throw new NameEmptyException();

        if (string.IsNullOrWhiteSpace(server.Description))
            throw new ArgumentException("ќписание сервера не может быть пустым");

        if (string.IsNullOrWhiteSpace(server.ServerPublicName))
            throw new ArgumentException("ѕубличное им€ сервера не может быть пустым");

        _serversStorage.RegisterServer(server);
        return Task.FromResult(new RegisterServerResponse());
    }
}