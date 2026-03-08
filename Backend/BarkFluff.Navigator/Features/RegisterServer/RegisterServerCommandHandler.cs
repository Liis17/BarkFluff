using BarkFluff.Navigator.Persistence;
using BarkFluff.Proto.Navigator;
using BarkFluff.Shared.Exceptions.Navigator;

using MediatR;

namespace BarkFluff.Navigator.Features.RegisterServer;

public class RegisterServerCommandHandler : IRequestHandler<RegisterServerCommand, RegisterServerResponse>
{
    private readonly ServersStorage _serversStorage;
    private readonly ILogger<RegisterServerCommandHandler> _logger;

    public RegisterServerCommandHandler(ServersStorage serversStorage, ILogger<RegisterServerCommandHandler> logger)
    {
        _serversStorage = serversStorage;
        _logger = logger;
    }

    public Task<RegisterServerResponse> Handle(RegisterServerCommand request, CancellationToken cancellationToken)
    {
        var server = request.Server;

        _logger.LogInformation(
            "Регистрация сервера '{ServerName}' ({PublicName}). Beacon: {BeaconHost}:{BeaconPort}",
            server.Name,
            server.ServerPublicName,
            server.BeaconHost,
            server.BeaconPort
        );

        if (string.IsNullOrWhiteSpace(server.BeaconHost))
        {
            _logger.LogWarning("Попытка регистрации сервера с пустым BeaconHost. Имя: {ServerName}", server.Name);
            throw new BeaconHostEmptyException();
        }

        if (server.BeaconPort <= 0 || server.BeaconPort > 65535)
        {
            _logger.LogWarning(
                "Попытка регистрации сервера '{ServerName}' с неверным портом: {Port}",
                server.Name,
                server.BeaconPort
            );
            throw new ArgumentException("Порт должен быть в диапазоне от 1 до 65535");
        }

        if (string.IsNullOrWhiteSpace(server.Name))
        {
            _logger.LogWarning("Попытка регистрации сервера с пустым именем");
            throw new NameEmptyException();
        }

        if (string.IsNullOrWhiteSpace(server.Description))
        {
            _logger.LogWarning("Попытка регистрации сервера '{ServerName}' с пустым описанием", server.Name);
            throw new ArgumentException("Описание сервера не может быть пустым");
        }

        if (string.IsNullOrWhiteSpace(server.ServerPublicName))
        {
            _logger.LogWarning("Попытка регистрации сервера '{ServerName}' с пустым публичным именем", server.Name);
            throw new ArgumentException("Публичное имя сервера не может быть пустым");
        }

        _serversStorage.RegisterServer(server);

        _logger.LogInformation(
            "Сервер '{ServerName}' ({PublicName}) успешно зарегистрирован",
            server.Name,
            server.ServerPublicName
        );

        return Task.FromResult(new RegisterServerResponse());
    }
}