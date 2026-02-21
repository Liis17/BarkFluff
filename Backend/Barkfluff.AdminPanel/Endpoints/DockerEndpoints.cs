using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
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
            .WithName("Docker")
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
    }
}
