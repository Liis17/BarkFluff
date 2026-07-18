using BarkFluff.Navigator.Persistence;
using BarkFluff.Proto.Navigator;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Navigator.Features.GetServerByName;

public class GetServerByNameQueryHandler : IRequestHandler<GetServerByNameQuery, GetServerByNameResponse>
{
    private readonly ServersStorage _serversStorage;
    private readonly ILogger<GetServerByNameQueryHandler> _logger;

    public GetServerByNameQueryHandler(ServersStorage serversStorage, ILogger<GetServerByNameQueryHandler> logger)
    {
        _serversStorage = serversStorage;
        _logger = logger;
    }

    public async Task<GetServerByNameResponse> Handle(GetServerByNameQuery request, CancellationToken cancellationToken)
    {
        var normalized = request.ServerName.Trim().ToLowerInvariant();
        var server = await _serversStorage.GetByServerNameAsync(normalized, cancellationToken);

        if (server == null)
        {
            _logger.LogDebug("GetServerByName({ServerName}) — не найдено или протухло", normalized);
            return new GetServerByNameResponse { Found = false };
        }

        var info = new ServerInfo
        {
            Name = server.Name,
            Description = server.Description,
            ServerPublicName = server.ServerPublicName,
            Location = server.Location,
            AccountsCount = 0,
            BeaconUri = new ServiceEndpoint { Host = server.BeaconHost, Port = server.BeaconPort },
            Color = new ServerColor { LiteHex = server.ColorLiteHex, MainHex = server.ColorMainHex, HardHex = server.ColorHardHex },
            ServerName = server.ServerName ?? string.Empty,
            FederationEndpoint = server.FederationEndpoint ?? string.Empty,
        };

        if (server.TlsSpkiSha256 != null)
            info.TlsSpkiSha256.AddRange(server.TlsSpkiSha256);

        if (server.FederationProtocolVersions != null)
            info.FederationProtocolVersions.AddRange(server.FederationProtocolVersions);

        if (server.SigningKeys != null)
        {
            info.SigningKeys.AddRange(server.SigningKeys.Select(k => new NavigatorSigningKey
            {
                KeyId = k.KeyId,
                PublicKey = ByteString.CopyFrom(Convert.FromBase64String(k.PublicKeyBase64)),
                ExpiredAt = k.ExpiredAt.HasValue
                    ? Timestamp.FromDateTime(DateTime.SpecifyKind(k.ExpiredAt.Value, DateTimeKind.Utc))
                    : null,
            }));
        }

        return new GetServerByNameResponse { Found = true, Server = info };
    }
}
