namespace BarkFluff.Navigator.Host;

using Features.ListServers;
using Grpc.Core;
using MediatR;
using Proto.Navigator;
using BarkFluff.Navigator.Persistence;
using Features.RegisterServer;
using BarkFluff.GrpcServer.XAuth;

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
            CreatedAt = DateTime.UtcNow,
            AddedBy = _userContext.IsAuthenticated ? _userContext.UserId.ToString() : "Anonymous"
        };
        var command = new Features.RegisterServer.RegisterServerCommand { Server = domainServer };
        return await _mediator.Send(command, context.CancellationToken);
    }
}