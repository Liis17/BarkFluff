using Barkfluff.AdminPanel.Middleware;
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
            .WithTags("Docker");

        // Получить список всех контейнеров
        group.MapGet("/containers", async (
            DockerService dockerService) =>
        {
            var containers = await dockerService.GetContainersAsync();
            return Results.Ok(containers);
        })
        .WithName("GetContainers")
        .WithOpenApi();

        // Получить статус конкретного контейнера
        group.MapGet("/containers/{name}/status", async (
            DockerService dockerService,
            string name) =>
        {
            var status = await dockerService.GetContainerStatusAsync(name);
            return status != null ? Results.Ok(status) : Results.NotFound($"Контейнер {name} не найден");
        })
        .WithName("GetContainerStatus")
        .WithOpenApi();

        group.MapGet("/containers/admin-panel/update-status", async (
            DockerService dockerService,
            DockerRegistryService dockerRegistryService) =>
        {
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
            string name) =>
        {
            var result = await dockerService.StartContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("StartContainer")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerControl);

        // Остановить контейнер
        group.MapPost("/containers/{name}/stop", async (
            DockerService dockerService,
            string name) =>
        {
            var result = await dockerService.StopContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("StopContainer")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerControl);

        // Перезапустить контейнер
        group.MapPost("/containers/{name}/restart", async (
            DockerService dockerService,
            string name) =>
        {
            var result = await dockerService.RestartContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartContainer")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerControl);

        // Обновить образ и пересоздать контейнер
        group.MapPost("/containers/{name}/pull", async (
            DockerService dockerService,
            string name) =>
        {
            var result = await dockerService.PullImageAndRecreateContainerAsync(name);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("PullImageAndRecreateContainer")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerControl);

        // Перезапустить админ-панель
        group.MapPost("/containers/admin-panel/restart-own", async (
            DockerService dockerService) =>
        {
            var result = await dockerService.RestartAdminPanelAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartAdminPanel")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerAdminPanelRestart);

        // Обновить админ-панель
        group.MapPost("/containers/admin-panel/update-own", async (
            DockerService dockerService) =>
        {
            var result = await dockerService.UpdateAdminPanelAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("UpdateAdminPanel")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerAdminPanelUpdate);

        // Ветки обновлений сервисов из docker-compose.yml
        group.MapGet("/branches", async (
            DockerService dockerService,
            ComposeImageService composeImageService) =>
        {
            IReadOnlyDictionary<string, ComposeImageInfo> images;
            try
            {
                images = await composeImageService.GetImagesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // compose-файл не смонтирован — страница просто не покажет выбор ветки
                return Results.Ok(Array.Empty<object>());
            }

            var runningImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var container in await dockerService.GetContainersAsync())
                    runningImages[container.Name.TrimStart('/')] = container.Image;
            }
            catch
            {
                // Docker недоступен — покажем только то, что записано в compose
            }

            var result = images.Values
                .OrderBy(image => image.Service, StringComparer.Ordinal)
                .Select(image => new
                {
                    service = image.Service,
                    container = image.Service,
                    branch = image.Branch,
                    runningBranch = runningImages.TryGetValue(image.Service, out var runningImage)
                        ? ComposeImageService.BranchFromImage(runningImage)
                        : null,
                    branches = ComposeImageService.Branches
                });

            return Results.Ok(result);
        })
        .WithName("GetContainerBranches")
        .WithOpenApi();

        // Переключить сервис на другую ветку обновлений и сразу обновить образ
        group.MapPost("/containers/{name}/branch", async (
            DockerService dockerService,
            ComposeImageService composeImageService,
            DockerRegistryService dockerRegistryService,
            string name,
            ContainerBranchRequestDto request) =>
        {
            var branch = request.Branch?.Trim() ?? string.Empty;
            if (!ComposeImageService.IsKnownBranch(branch))
                return Results.BadRequest(new ContainerActionResponseDto
                {
                    Success = false,
                    Message = $"Неизвестная ветка {branch}"
                });

            var serviceName = DockerService.ConvertContainerNameToServiceName(name);

            IReadOnlyDictionary<string, ComposeImageInfo> images;
            try
            {
                images = await composeImageService.GetImagesAsync();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ContainerActionResponseDto
                {
                    Success = false,
                    Message = "Не удалось прочитать docker-compose.yml",
                    ErrorDetails = ex.Message
                });
            }

            if (!images.TryGetValue(serviceName, out var image))
                return Results.NotFound(new ContainerActionResponseDto
                {
                    Success = false,
                    Message = $"Сервис {serviceName} не найден в docker-compose.yml или его образ не из реестра BarkFluff"
                });

            var status = await dockerService.GetContainerStatusAsync(name);
            var runningBranch = ComposeImageService.BranchFromImage(status?.Image);
            if (image.Branch == branch && runningBranch == branch)
                return Results.Ok(new ContainerActionResponseDto
                {
                    Success = true,
                    Message = $"{name} уже работает на ветке {branch}"
                });

            var repository = ComposeImageService.Repository(image.BaseRepository, branch);
            if (!await dockerRegistryService.RepositoryExistsAsync(repository))
                return Results.BadRequest(new ContainerActionResponseDto
                {
                    Success = false,
                    Message = $"Образ {repository} не найден в реестре (или реестр недоступен)"
                });

            string previousCompose;
            try
            {
                previousCompose = await composeImageService.SetBranchAsync(serviceName, branch);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ContainerActionResponseDto
                {
                    Success = false,
                    Message = "Не удалось изменить docker-compose.yml",
                    ErrorDetails = ex.Message
                });
            }

            var isAdminPanel = string.Equals(name, "admin-panel", StringComparison.OrdinalIgnoreCase);
            var result = isAdminPanel
                ? await dockerService.UpdateAdminPanelAsync()
                : await dockerService.PullImageAndRecreateContainerAsync(name);

            if (!result.Success)
            {
                await composeImageService.RestoreAsync(previousCompose);
                result.Message = $"{result.Message}. Ветка в docker-compose.yml возвращена на {image.Branch}";
                return Results.BadRequest(result);
            }

            result.Message = isAdminPanel
                ? $"Админ-панель переключается на ветку {branch}"
                : $"{name} переключён на ветку {branch}, контейнер пересоздан";
            return Results.Ok(result);
        })
        .WithName("SetContainerBranch")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerBranch, context => $"container={context.Request.RouteValues["name"]}");

        // Перезапустить все сервисы BarkFluff
        group.MapPost("/containers/restart-all", async (
            DockerService dockerService) =>
        {
            var result = await dockerService.RestartAllServicesAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("RestartAllContainers")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerRestartAll);

        // Обновить все сервисы BarkFluff
        group.MapPost("/containers/update-all", async (
            DockerService dockerService) =>
        {
            var result = await dockerService.UpdateAllServicesAsync();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("UpdateAllContainers")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerUpdateAll);
    }
}
