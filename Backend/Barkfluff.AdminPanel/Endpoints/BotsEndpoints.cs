using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Google.Protobuf;

using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

public static class BotsEndpoints
{
    private const string BotNotFoundErrorCode = "4F8A2D1C-9B3E-47A6-8C5D-1E7F0B2A9D34";
    private const string UserNotFoundErrorCode = "A4DAB334-1067-4838-A782-C4257DC838F7";
    private const string UsernameExistErrorCode = "DB157CD8-98A3-4A35-9857-33821813D422";

    public static void MapBotsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bots")
            .WithTags("Bots")
            .RequirePermission(AdminPermissions.BotsManage);

        // GET /api/bots — все боты с preview аватарки
        group.MapGet("/", async (
            BotsServerApi.BotsServerApiClient botsClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            try
            {
                var response = await botsClient.ListBotsAsync(new ListBotsRequest(),
                    cancellationToken: context.RequestAborted);
                var usersById = new Dictionary<long, BarkFluff.Proto.Users.User>();

                if (response.Bots.Count > 0)
                {
                    var usersResponse = await usersClient.ListByIdsAsync(
                        new ListByIdsRequest { Ids = { response.Bots.Select(b => b.Id) } },
                        cancellationToken: context.RequestAborted);
                    usersById = usersResponse.Users.ToDictionary(u => u.Id);
                }

                var bots = response.Bots.Select(b => new
                {
                    id = b.Id,
                    username = b.Username,
                    name = b.Name,
                    profilePicturePreview = usersById.GetValueOrDefault(b.Id)?.ProfilePicturePreview ?? string.Empty,
                    ownerUserId = b.OwnerUserId,
                    systemRole = b.SystemRole,
                    createdAt = b.CreatedAt?.ToDateTime()
                });

                return Results.Ok(bots);
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("GetAllBots");

        // GET /api/bots/{id} — профиль бота, включая полный аватар и постер
        group.MapGet("/{id:long}", async (
            long id,
            BotsServerApi.BotsServerApiClient botsClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            try
            {
                var botsTask = botsClient.ListBotsAsync(
                    new ListBotsRequest(), cancellationToken: context.RequestAborted);
                var userTask = usersClient.GetByIdAsync(
                    new GetByIdRequest { UserId = id }, cancellationToken: context.RequestAborted);
                var posterTask = usersClient.GetProfilePosterServerAsync(
                    new GetProfilePosterServerRequest { UserId = id },
                    cancellationToken: context.RequestAborted);

                await Task.WhenAll(botsTask.ResponseAsync, userTask.ResponseAsync, posterTask.ResponseAsync);

                var bot = botsTask.ResponseAsync.Result.Bots.FirstOrDefault(b => b.Id == id);
                var user = userTask.ResponseAsync.Result.User;
                if (bot is null || user is null || !user.IsBot)
                    return Results.NotFound();

                return Results.Ok(new
                {
                    profile = new
                    {
                        id = user.Id,
                        firstName = user.FirstName,
                        username = user.Username,
                        profilePicture = user.ProfilePicture,
                        profilePicturePreview = user.ProfilePicturePreview,
                        profilePosterUrl = posterTask.ResponseAsync.Result.PosterUrl,
                        registrationDate = user.RegistrationDate?.ToDateTime(),
                        ownerUserId = bot.OwnerUserId,
                        systemRole = bot.SystemRole,
                        createdAt = bot.CreatedAt?.ToDateTime()
                    }
                });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("GetBotProfile");

        // POST /api/bots — создать системного бота (JSON: username, name). Токен показывается один раз.
        group.MapPost("/", async (
            CreateBotRequestBody body,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Username and name are required");

            try
            {
                var response = await botsClient.CreateSystemBotAsync(new CreateSystemBotRequest
                {
                    Username = body.Username.Trim(),
                    Name = body.Name.Trim()
                }, cancellationToken: context.RequestAborted);

                return Results.Ok(new { botId = response.BotId, token = response.Token });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("CreateSystemBot");

        // PUT /api/bots/{id}/profile — изменить имя и username
        group.MapPut("/{id:long}/profile", async (
            long id,
            UpdateBotProfileBody body,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            if (body is null)
                return Results.BadRequest("Invalid request body");

            try
            {
                await botsClient.UpdateBotProfileAsync(new UpdateBotProfileRequest
                {
                    BotId = id,
                    Name = body.Name?.Trim() ?? string.Empty,
                    Username = body.Username?.Trim() ?? string.Empty
                }, cancellationToken: context.RequestAborted);

                return Results.Ok(new { success = true });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("UpdateBotProfile");

        // POST /api/bots/{id}/avatar
        group.MapPost("/{id:long}/avatar", async (
            long id,
            HttpRequest request,
            BotsServerApi.BotsServerApiClient botsClient,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            try
            {
                if (!await BotExists(botsClient, id, context.RequestAborted))
                    return Results.NotFound();

                var form = await request.ReadFormAsync(context.RequestAborted);
                var file = form.Files.GetFile("avatar");
                if (file is null || file.Length == 0)
                    return Results.BadRequest("No avatar file provided");

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, context.RequestAborted);

                var uploadResponse = await filesClient.UploadAvatarServerAsync(new UploadAvatarServerRequest
                {
                    ImageData = ByteString.CopyFrom(ms.ToArray()),
                    Filename = file.FileName,
                    UserId = id
                }, cancellationToken: context.RequestAborted);

                await usersClient.SetProfilePictureServerAsync(new SetProfilePictureServerRequest
                {
                    UserId = id,
                    ProfilePictureUrl = uploadResponse.FileUrl,
                    ProfilePicturePreviewUrl = uploadResponse.PreviewUrl
                }, cancellationToken: context.RequestAborted);

                return Results.Ok(new
                {
                    fileUrl = uploadResponse.FileUrl,
                    previewUrl = uploadResponse.PreviewUrl,
                    fileId = uploadResponse.FileId
                });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .DisableAntiforgery()
        .WithName("UploadBotAvatar");

        // POST /api/bots/{id}/poster
        group.MapPost("/{id:long}/poster", async (
            long id,
            HttpRequest request,
            BotsServerApi.BotsServerApiClient botsClient,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            try
            {
                if (!await BotExists(botsClient, id, context.RequestAborted))
                    return Results.NotFound();

                var form = await request.ReadFormAsync(context.RequestAborted);
                var file = form.Files.GetFile("poster");
                if (file is null || file.Length == 0)
                    return Results.BadRequest("No poster file provided");

                if (file.Length > 15 * 1024 * 1024)
                    return Results.BadRequest("File too large (max 15 MB)");

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, context.RequestAborted);

                var uploadResponse = await filesClient.UploadPosterServerAsync(new UploadPosterServerRequest
                {
                    ImageData = ByteString.CopyFrom(ms.ToArray()),
                    Filename = file.FileName,
                    UserId = id
                }, cancellationToken: context.RequestAborted);

                await usersClient.SetProfilePosterServerAsync(new SetProfilePosterServerRequest
                {
                    UserId = id,
                    PosterFileId = uploadResponse.FileId
                }, cancellationToken: context.RequestAborted);

                return Results.Ok(new
                {
                    posterUrl = uploadResponse.FileUrl,
                    fileId = uploadResponse.FileId
                });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .DisableAntiforgery()
        .WithName("UploadBotPoster");

        // POST /api/bots/{id}/token — текущий токен без ротации
        group.MapPost("/{id:long}/token", async (
            long id,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";

            try
            {
                var response = await botsClient.GetBotTokenAsync(
                    new GetBotTokenRequest { BotId = id },
                    cancellationToken: context.RequestAborted);
                return Results.Ok(new { token = response.Token });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("GetCurrentBotToken");

        // POST /api/bots/{id}/regenerate-token — новый токен (старый отзывается)
        group.MapPost("/{id:long}/regenerate-token", async (
            long id,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            try
            {
                var response = await botsClient.RegenerateTokenAsync(
                    new RegenerateTokenRequest { BotId = id },
                    cancellationToken: context.RequestAborted);

                return Results.Ok(new { token = response.Token });
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("RegenerateBotToken");

        // DELETE /api/bots/{id} — удалить бота (чаты сохраняются)
        group.MapDelete("/{id:long}", async (
            long id,
            BotsServerApi.BotsServerApiClient botsClient,
            HttpContext context) =>
        {
            try
            {
                await botsClient.DeleteBotAsync(
                    new DeleteBotRequest { BotId = id },
                    cancellationToken: context.RequestAborted);

                return Results.Ok();
            }
            catch (RpcException ex)
            {
                return MapGrpcError(ex);
            }
        })
        .WithName("DeleteBot");
    }

    private static async Task<bool> BotExists(
        BotsServerApi.BotsServerApiClient botsClient,
        long id,
        CancellationToken cancellationToken)
    {
        var response = await botsClient.ListBotsAsync(
            new ListBotsRequest(), cancellationToken: cancellationToken);
        return response.Bots.Any(b => b.Id == id);
    }

    private static IResult MapGrpcError(RpcException ex)
    {
        var errorCode = ex.Trailers.FirstOrDefault(t => t.Key == "x-error-code")?.Value;

        if (ex.StatusCode == StatusCode.InvalidArgument)
            return Results.BadRequest(ex.Status.Detail);

        if (ex.StatusCode == StatusCode.AlreadyExists || errorCode == UsernameExistErrorCode)
            return Results.Conflict(ex.Status.Detail);

        if (ex.StatusCode == StatusCode.NotFound ||
            errorCode is BotNotFoundErrorCode or UserNotFoundErrorCode)
        {
            return Results.NotFound();
        }

        return Results.Problem($"Ошибка gRPC: {ex.Status.Detail}");
    }

    public record CreateBotRequestBody(string? Username, string? Name);
    public record UpdateBotProfileBody(string? Name, string? Username);
}
