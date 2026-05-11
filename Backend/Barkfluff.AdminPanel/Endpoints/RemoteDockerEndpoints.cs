using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class RemoteDockerEndpoints
{
    public static void MapRemoteDockerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/remote")
            .WithTags("RemoteDocker");

        group.MapGet("/{server}/inspect/{containerName}", async (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server,
            string containerName) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var labels = await remoteDockerService.InspectContainerLabelsAsync(server, containerName);
            return Results.Ok(new { containerName, labels });
        })
        .WithName("InspectRemoteContainer")
        .WithOpenApi();

        group.MapGet("/{server}/config", (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var info = remoteDockerService.GetServerInfo(server);
            return Results.Ok(info);
        })
        .WithName("GetRemoteServerConfig")
        .WithOpenApi();

        group.MapGet("/{server}/containers", async (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var containers = await remoteDockerService.GetContainersStatusAsync(server);
            return Results.Ok(containers);
        })
        .WithName("GetRemoteContainers")
        .WithOpenApi();

        group.MapPost("/{server}/containers/{name}/start", async (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await remoteDockerService.StartAsync(server, name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("StartRemoteContainer")
        .WithOpenApi();

        group.MapPost("/{server}/containers/{name}/stop", async (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await remoteDockerService.StopAsync(server, name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("StopRemoteContainer")
        .WithOpenApi();

        group.MapPost("/{server}/containers/{name}/restart", async (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await remoteDockerService.RestartAsync(server, name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartRemoteContainer")
        .WithOpenApi();

        group.MapPost("/{server}/containers/{name}/pull", async (
            RemoteDockerService remoteDockerService,
            HttpContext context,
            string server,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await remoteDockerService.PullAndRecreateAsync(server, name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("PullRemoteContainer")
        .WithOpenApi();
    }
}
