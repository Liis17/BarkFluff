using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BarkFluff.GrpcServer;

public static class PingEndpointExtensions
{
    public static IEndpointRouteBuilder MapPingEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ping", () => Results.Text("pong"))
            .AllowAnonymous();

        return endpoints;
    }
}
