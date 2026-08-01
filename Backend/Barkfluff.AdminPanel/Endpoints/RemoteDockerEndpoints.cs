using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class RemoteDockerEndpoints
{
    public static void MapRemoteDockerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/remote")
            .WithTags("RemoteDocker");

        group.MapGet("/servers", (RemoteDockerService service, HttpContext context) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            return Results.Ok(service.GetServers());
        });

        group.MapPost("/servers", async (RemoteDockerService service, HttpContext context, SaveRemoteServerRequest request,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            try
            {
                return Results.Created($"/api/remote/servers", await service.CreateServerAsync(request, cancellationToken));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (Exception ex) { return Results.BadRequest(new { message = $"Не удалось подключиться по SSH: {ex.Message}" }); }
        });

        group.MapPut("/servers/{serverId:guid}", async (RemoteDockerService service, HttpContext context, Guid serverId,
            SaveRemoteServerRequest request, CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            try
            {
                var updated = await service.UpdateServerAsync(serverId, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (Exception ex) { return Results.BadRequest(new { message = $"Не удалось подключиться по SSH: {ex.Message}" }); }
        });

        group.MapDelete("/servers/{serverId:guid}", (RemoteDockerService service, HttpContext context, Guid serverId) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            return service.DeleteServer(serverId) ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/servers/{serverId:guid}/discover", async (RemoteDockerService service, HttpContext context, Guid serverId,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            try { return Results.Ok(await service.DiscoverContainersAsync(serverId, cancellationToken)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        group.MapGet("/servers/{serverId:guid}/containers", async (RemoteDockerService service, HttpContext context, Guid serverId,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            try { return Results.Ok(await service.GetContainersStatusAsync(serverId, cancellationToken)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        group.MapPost("/servers/{serverId:guid}/containers", async (RemoteDockerService service, HttpContext context, Guid serverId,
            AddRemoteContainerRequest request, CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            try
            {
                var container = await service.AddContainerAsync(serverId, request.ContainerName, cancellationToken);
                return container is null ? Results.NotFound(new { message = "Контейнер не найден на сервере" }) : Results.Ok(container);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        group.MapDelete("/servers/{serverId:guid}/containers/{containerId:guid}",
            (RemoteDockerService service, HttpContext context, Guid serverId, Guid containerId) =>
            {
                if (!IsAuthorized(context)) return Results.Unauthorized();
                return service.DeleteContainer(serverId, containerId) ? Results.NoContent() : Results.NotFound();
            });

        group.MapPost("/servers/{serverId:guid}/containers/{containerId:guid}/{action}", async (
            RemoteDockerService service, HttpContext context, Guid serverId, Guid containerId, string action,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context)) return Results.Unauthorized();
            try
            {
                var result = await service.ExecuteActionAsync(serverId, containerId, action, cancellationToken);
                return result.Success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
    }

    private static bool IsAuthorized(HttpContext context) => context.Items["AuthToken"] is AuthToken;
}
