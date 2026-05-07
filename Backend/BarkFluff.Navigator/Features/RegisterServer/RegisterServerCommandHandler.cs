using System.Net;
using System.Text.RegularExpressions;

using BarkFluff.Navigator.Persistence;
using BarkFluff.Proto.Navigator;
using BarkFluff.Shared.Exceptions.Navigator;

using MediatR;

namespace BarkFluff.Navigator.Features.RegisterServer;

public partial class RegisterServerCommandHandler : IRequestHandler<RegisterServerCommand, RegisterServerResponse>
{
    private const int MaxNameLength = 64;
    private const int MaxPublicNameLength = 64;
    private const int MaxDescriptionLength = 512;
    private const int MaxLocationLength = 128;

    [GeneratedRegex(@"^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(
        @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])$",
        RegexOptions.Compiled)]
    private static partial Regex HostnameRegex();

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

        if (server.BeaconHost.Length > 253
            || (!HostnameRegex().IsMatch(server.BeaconHost) && !IPAddress.TryParse(server.BeaconHost, out _)))
        {
            _logger.LogWarning(
                "Попытка регистрации сервера '{ServerName}' с некорректным BeaconHost: {BeaconHost}",
                server.Name,
                server.BeaconHost
            );
            throw new InvalidBeaconHostException();
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

        if (server.Name.Length > MaxNameLength)
        {
            _logger.LogWarning(
                "Попытка регистрации сервера с превышением длины имени: {Length} > {Max}",
                server.Name.Length,
                MaxNameLength
            );
            throw new ArgumentException($"Имя сервера не должно превышать {MaxNameLength} символов");
        }

        if (string.IsNullOrWhiteSpace(server.Description))
        {
            _logger.LogWarning("Попытка регистрации сервера '{ServerName}' с пустым описанием", server.Name);
            throw new ArgumentException("Описание сервера не может быть пустым");
        }

        if (server.Description.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"Описание не должно превышать {MaxDescriptionLength} символов");
        }

        if (string.IsNullOrWhiteSpace(server.ServerPublicName))
        {
            _logger.LogWarning("Попытка регистрации сервера '{ServerName}' с пустым публичным именем", server.Name);
            throw new ArgumentException("Публичное имя сервера не может быть пустым");
        }

        if (server.ServerPublicName.Length > MaxPublicNameLength)
        {
            throw new ArgumentException($"Публичное имя не должно превышать {MaxPublicNameLength} символов");
        }

        if (server.Location.Length > MaxLocationLength)
        {
            throw new ArgumentException($"Location не должно превышать {MaxLocationLength} символов");
        }

        ValidateHexColor(server.ColorLiteHex);
        ValidateHexColor(server.ColorMainHex);
        ValidateHexColor(server.ColorHardHex);

        _serversStorage.RegisterServer(server);

        _logger.LogInformation(
            "Сервер '{ServerName}' ({PublicName}) успешно зарегистрирован",
            server.Name,
            server.ServerPublicName
        );

        return Task.FromResult(new RegisterServerResponse());
    }

    private static void ValidateHexColor(string color)
    {
        if (string.IsNullOrEmpty(color))
            return;

        if (!HexColorRegex().IsMatch(color))
            throw new InvalidHexColorException();
    }
}
