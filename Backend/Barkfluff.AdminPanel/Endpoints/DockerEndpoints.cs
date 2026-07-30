using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

/// <summary>
/// Эндпоинты для управления Docker контейнерами
/// </summary>
public static class DockerEndpoints
{
    public static void MapDockerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/docker")
            .WithTags("Docker");

        // Получить список всех контейнеров
        group.MapGet("/containers", async (
            DockerService dockerService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var containers = await dockerService.GetContainersAsync();
            return Results.Ok(containers);
        })
        .WithName("GetContainers")
        .WithOpenApi();

        // Получить статус конкретного контейнера
        group.MapGet("/containers/{name}/status", async (
            DockerService dockerService,
            HttpContext context,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var status = await dockerService.GetContainerStatusAsync(name);
            return status != null ? Results.Ok(status) : Results.NotFound($"Контейнер {name} не найден");
        })
        .WithName("GetContainerStatus")
        .WithOpenApi();

        group.MapGet("/containers/admin-panel/update-status", async (
            DockerService dockerService,
            DockerRegistryService dockerRegistryService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var status = await dockerService.GetContainerStatusAsync("admin-panel");
            if (status is null)
                return Results.NotFound("Контейнер admin-panel не найден");

            var versionStatus = await dockerRegistryService.GetVersionStatusAsync(status.Image, status.ImageDigest);
            return Results.Ok(versionStatus);
        })
        .WithName("GetAdminPanelUpdateStatus")
        .WithOpenApi();

        // Запустить контейнер
        group.MapPost("/containers/{name}/start", async (
            DockerService dockerService,
            HttpContext context,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.StartContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("StartContainer")
        .WithOpenApi();

        // Остановить контейнер
        group.MapPost("/containers/{name}/stop", async (
            DockerService dockerService,
            HttpContext context,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.StopContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("StopContainer")
        .WithOpenApi();

        // Перезапустить контейнер
        group.MapPost("/containers/{name}/restart", async (
            DockerService dockerService,
            HttpContext context,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.RestartContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartContainer")
        .WithOpenApi();

        // Обновить образ и пересоздать контейнер
        group.MapPost("/containers/{name}/pull", async (
            DockerService dockerService,
            HttpContext context,
            string name) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.PullImageAndRecreateContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("PullImageAndRecreateContainer")
        .WithOpenApi();

        // Перезапустить админ-панель
        group.MapPost("/containers/admin-panel/restart-own", async (
            DockerService dockerService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.RestartAdminPanelAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartAdminPanel")
        .WithOpenApi();

        // Обновить админ-панель
        group.MapPost("/containers/admin-panel/update-own", async (
            DockerService dockerService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.UpdateAdminPanelAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("UpdateAdminPanel")
        .WithOpenApi();

        // Перезапустить все сервисы BarkFluff
        group.MapPost("/containers/restart-all", async (
            DockerService dockerService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.RestartAllServicesAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartAllContainers")
        .WithOpenApi();

        // Обновить все сервисы BarkFluff
        group.MapPost("/containers/update-all", async (
            DockerService dockerService,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var result = await dockerService.UpdateAllServicesAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("UpdateAllContainers")
        .WithOpenApi();
    }
}
