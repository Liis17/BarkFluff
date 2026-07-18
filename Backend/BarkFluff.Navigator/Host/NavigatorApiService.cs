namespace BarkFluff.Navigator.Host;

using BarkFluff.GrpcServer.XAuth;

using Features.GetServerByName;
using Features.ListServers;

using Grpc.Core;

using MediatR;

using Proto.Navigator;

public class NavigatorApiService : NavigatorApi.NavigatorApiBase
{
    private readonly IMediator _mediator;
    private readonly UserContext _userContext;

    public NavigatorApiService(IMediator mediator, UserContext userContext)
    {
        _mediator = mediator;
        _userContext = userContext;
    }

    public override async Task<ListServersResponse> ListServers(ListServersRequest request, ServerCallContext context)
    {
        var command = new ListServersQuery();

        return await _mediator.Send(command, context.CancellationToken);
    }

    public override async Task<RegisterServerResponse> RegisterServer(RegisterServerRequest request, ServerCallContext context)
    {
        var protoServer = request.Server;
        var domainServer = new BarkFluff.Navigator.Domain.ServerInfo
        {
            BeaconHost = protoServer?.BeaconUri?.Host ?? string.Empty,
            BeaconPort = protoServer?.BeaconUri?.Port ?? 0,
            Name = protoServer?.Name ?? string.Empty,
            Description = protoServer?.Description ?? string.Empty,
            ServerPublicName = protoServer?.ServerPublicName ?? string.Empty,
            Location = protoServer?.Location ?? string.Empty,
            ColorLiteHex = protoServer?.Color?.LiteHex ?? string.Empty,
            ColorMainHex = protoServer?.Color?.MainHex ?? string.Empty,
            ColorHardHex = protoServer?.Color?.HardHex ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            AddedBy = _userContext.IsAuthenticated ? _userContext.UserId.ToString() : "Anonymous",
            ServerName = string.IsNullOrWhiteSpace(protoServer?.ServerName) ? null : protoServer.ServerName,
            FederationEndpoint = string.IsNullOrWhiteSpace(protoServer?.FederationEndpoint) ? null : protoServer.FederationEndpoint,
            TlsSpkiSha256 = protoServer?.TlsSpkiSha256.Count > 0 ? protoServer.TlsSpkiSha256.ToArray() : null,
            FederationProtocolVersions = protoServer?.FederationProtocolVersions.Count > 0 ? protoServer.FederationProtocolVersions.ToArray() : null,
            SigningKeys = protoServer?.SigningKeys.Count > 0
                ? protoServer.SigningKeys.Select(k => new BarkFluff.Navigator.Domain.NavigatorSigningKeyInfo
                {
                    KeyId = k.KeyId,
                    PublicKeyBase64 = Convert.ToBase64String(k.PublicKey.ToByteArray()),
                    ExpiredAt = k.ExpiredAt != null ? k.ExpiredAt.ToDateTime() : null,
                }).ToList()
                : null,
        };
        var command = new Features.RegisterServer.RegisterServerCommand { Server = domainServer };
        return await _mediator.Send(command, context.CancellationToken);
    }

    public override async Task<GetServerByNameResponse> GetServerByName(GetServerByNameRequest request, ServerCallContext context)
    {
        var query = new GetServerByNameQuery { ServerName = request.ServerName };
        return await _mediator.Send(query, context.CancellationToken);
    }
}