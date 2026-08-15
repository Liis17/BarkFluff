using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

using System.Net.WebSockets;

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

        group.MapGet("/servers/{serverId:guid}/console", async (RemoteDockerService service, HttpContext context,
            Guid serverId, ILogger<RemoteDockerService> logger, CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "Ожидался WebSocket-запрос" }, cancellationToken);
                return;
            }

            if (!IsSameOrigin(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Недопустимый источник WebSocket-соединения" }, cancellationToken);
                return;
            }

            IRemoteSshShell? shell = null;
            try
            {
                shell = await service.OpenShellAsync(serverId, cancellationToken);
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await RunConsoleAsync(socket, shell, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The browser or the request disconnected.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка открытия SSH-консоли на удалённом сервере {ServerId}", serverId);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await context.Response.WriteAsJsonAsync(new { message = "Не удалось открыть SSH-консоль" });
                }
            }
            finally
            {
                if (shell is not null)
                    await shell.DisposeAsync();
            }
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

    private static bool IsSameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
            return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;
        if (!string.Equals(originUri.Scheme, context.Request.IsHttps ? "https" : "http", StringComparison.OrdinalIgnoreCase))
            return false;

        var requestHost = context.Request.Host;
        var requestPort = requestHost.Port ?? (context.Request.IsHttps ? 443 : 80);
        var originPort = originUri.IsDefaultPort
            ? (string.Equals(originUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : originUri.Port;

        return string.Equals(originUri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase)
            && originPort == requestPort;
    }

    private static async Task RunConsoleAsync(WebSocket socket, IRemoteSshShell shell, CancellationToken cancellationToken)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var inputTask = ForwardWebSocketInputAsync(socket, shell, sessionCts.Token);
        var outputTask = ForwardShellOutputAsync(socket, shell, sessionCts.Token);

        await Task.WhenAny(inputTask, outputTask);
        sessionCts.Cancel();

        try
        {
            await Task.WhenAll(inputTask, outputTask);
        }
        catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
        {
            // Closing either side ends the interactive session.
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Консоль закрыта", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The client may have already closed the connection.
            }
        }
    }

    private static async Task ForwardWebSocketInputAsync(WebSocket socket, IRemoteSshShell shell, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (!cancellationToken.IsCancellationRequested && (socket.State is WebSocketState.Open or WebSocketState.CloseReceived))
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return;
            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            if (result.Count > 0)
                await shell.WriteAsync(buffer, 0, result.Count, cancellationToken);
        }
    }

    private static async Task ForwardShellOutputAsync(WebSocket socket, IRemoteSshShell shell, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var count = await shell.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (count <= 0)
                return;

            await socket.SendAsync(new ArraySegment<byte>(buffer, 0, count), WebSocketMessageType.Text, true, cancellationToken);
        }
    }
}
