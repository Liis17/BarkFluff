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

        // Обновить образ и пересоздать контейнер (задача в очереди деплоя)
        group.MapPost("/containers/{name}/pull", (
            DeployJobService deployJobs,
            string name) =>
        {
            var service = DockerService.ConvertContainerNameToServiceName(name);
            var job = deployJobs.EnqueueUpdate([service]);
            return Results.Ok(new DeployJobStartDto
            {
                JobId = job.Id,
                Message = $"Обновление {name} поставлено в очередь"
            });
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
            DeployJobService deployJobs,
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

            var isAdminPanel = string.Equals(name, "admin-panel", StringComparison.OrdinalIgnoreCase);
            var job = deployJobs.EnqueueBranchSwitch(serviceName, branch);

            return Results.Ok(new DeployJobStartDto
            {
                JobId = job.Id,
                Message = isAdminPanel
                    ? $"Админ-панель переключается на ветку {branch}"
                    : $"{name} переключается на ветку {branch} (задача в очереди)"
            });
        })
        .WithName("SetContainerBranch")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerBranch, context => $"container={context.Request.RouteValues["name"]}");

        // Перезапустить все сервисы BarkFluff (задача в очереди деплоя)
        group.MapPost("/containers/restart-all", (
            DeployJobService deployJobs) =>
        {
            var job = deployJobs.EnqueueRestart(DeployJobService.DeployOrder);
            return Results.Ok(new DeployJobStartDto
            {
                JobId = job.Id,
                Message = "Перезапуск всех сервисов поставлен в очередь"
            });
        })
        .WithName("RestartAllContainers")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerRestartAll);

        // Обновить все сервисы BarkFluff (задача в очереди деплоя)
        group.MapPost("/containers/update-all", (
            DeployJobService deployJobs) =>
        {
            var job = deployJobs.EnqueueUpdate(DeployJobService.DeployOrder);
            return Results.Ok(new DeployJobStartDto
            {
                JobId = job.Id,
                Message = "Обновление всех сервисов поставлено в очередь"
            });
        })
        .WithName("UpdateAllContainers")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerDeploy)
        .RequireStepUp(StepUpActions.DockerUpdateAll);

        // Обновить перечисленные контейнеры (задача в очереди деплоя; заменяет браузерный цикл)
        group.MapPost("/containers/update-many", (
            DeployJobService deployJobs,
            UpdateContainersRequestDto request) =>
        {
            var services = (request.Containers ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => DockerService.ConvertContainerNameToServiceName(c.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (services.Count == 0)
                return Results.BadRequest(new ContainerActionResponseDto
                {
                    Success = false,
                    Message = "Список сервисов пуст"
                });

            var job = deployJobs.EnqueueUpdate(services);
            return Results.Ok(new DeployJobStartDto
            {
                JobId = job.Id,
                Message = $"Обновление {services.Count} сервисов поставлено в очередь"
            });
        })
        .WithName("UpdateContainers")
        .WithOpenApi()
        .RequirePermission(AdminPermissions.DockerControl);

        // Задачи деплоя: активные и недавние
        group.MapGet("/deploy/jobs", (
            DeployJobService deployJobs) =>
        {
            return Results.Ok(deployJobs.GetRecentJobs().Select(ToDto));
        })
        .WithName("GetDeployJobs")
        .WithOpenApi();

        // Статус задачи деплоя
        group.MapGet("/deploy/jobs/{id:guid}", (
            DeployJobService deployJobs,
            Guid id) =>
        {
            var job = deployJobs.GetJob(id);
            return job is not null ? Results.Ok(ToDto(job)) : Results.NotFound($"Задача {id} не найдена");
        })
        .WithName("GetDeployJob")
        .WithOpenApi();
    }

    /// <summary>Проекция задачи в JSON-ответ со строковыми состояниями</summary>
    private static object ToDto(DeployJob job) => new
    {
        id = job.Id,
        kind = job.Kind.ToString(),
        state = job.State.ToString(),
        error = job.Error,
        createdAtUtc = job.CreatedAtUtc,
        startedAtUtc = job.StartedAtUtc,
        finishedAtUtc = job.FinishedAtUtc,
        steps = job.Steps.Select(step => new
        {
            service = step.Service,
            branch = step.Branch,
            state = step.State.ToString(),
            message = step.Message,
            rolledBack = step.RolledBack
        })
    };
}
