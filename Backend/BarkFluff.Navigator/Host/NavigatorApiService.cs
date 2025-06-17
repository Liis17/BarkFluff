namespace BarkFluff.Navigator.Host;

using Features.ListServers;
using Grpc.Core;
using MediatR;
using Proto.Navigator;

public class NavigatorApiService(IMediator mediator) : NavigatorApi.NavigatorApiBase
{
    public override async Task<ListServersResponse> ListServers(ListServersRequest request, ServerCallContext context)
    {
        var command = new ListServersQuery();
        
        return await mediator.Send(command, context.CancellationToken);
    }
}