using System.Net;

using BarkFluff.GrpcServer;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.GrpcServer.Tests;

public class PingEndpointTests
{
    [Fact]
    public async Task Ping_returns_pong_without_authentication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.MapPingEndpoint();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Be("pong");

        var pingEndpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(endpoint => endpoint.DisplayName?.Contains("GET /ping", StringComparison.Ordinal) == true);
        pingEndpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
    }
}
