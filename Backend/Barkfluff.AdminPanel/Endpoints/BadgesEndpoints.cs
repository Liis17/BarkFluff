using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Google.Protobuf;
using Grpc.Core;

namespace Barkfluff.AdminPanel.Endpoints;

public static class BadgesEndpoints
{
    public static void MapBadgesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/badges")
            .WithTags("Badges")
            .RequirePermission(AdminPermissions.BadgesManage);

        // GET /api/badges — все бейджи (включая неактивные)
        group.MapGet("/", async (
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
            var response = await usersClient.GetAllBadgesAsync(new GetAllBadgesRequest
            {
                IncludeInactive = true
            });

            var badges = response.Badges.Select(b => new
            {
                id = b.Id,
                name = b.Name,
                description = b.Description,
                imageUrl = b.ImageUrl,
                miniImageUrl = b.MiniImageUrl,
                isActive = b.IsActive,
                createdDate = b.CreatedDate?.ToDateTime()
            });

            return Results.Ok(badges);
        })
        .WithName("GetAllBadges");

        // POST /api/badges — создать бейдж с полной и миниатюрной картинками.
        group.MapPost("/", async (
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient,
            FilesServerApi.FilesServerApiClient filesClient) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();

            var name = form["name"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Name is required");

            var description = form["description"].FirstOrDefault() ?? string.Empty;
            var isActive = bool.TryParse(form["isActive"].FirstOrDefault(), out var active) && active;

            var imageFile = form.Files.GetFile("image");
            var miniImageFile = form.Files.GetFile("miniImage");
            var imageValidationError = ValidateCreateBadgeImages(imageFile, miniImageFile);
            if (imageValidationError is not null)
                return Results.BadRequest(imageValidationError);

            UploadBadgeImageResponse fullUpload;
            UploadBadgeImageResponse miniUpload;
            try
            {
                fullUpload = await UploadBadgeImageAsync(imageFile, BadgeImageKind.Full, filesClient);
                miniUpload = await UploadBadgeImageAsync(miniImageFile, BadgeImageKind.Mini, filesClient);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
            {
                return InvalidBadgeImageResponse(ex);
            }

            var createResponse = await usersClient.CreateBadgeAsync(new CreateBadgeRequest
            {
                Name = name,
                Description = description,
                ImageUrl = fullUpload.PermanentUrl,
                MiniImageUrl = miniUpload.PermanentUrl
            });

            // Если isActive = false — сразу деактивируем
            if (!isActive)
            {
                await usersClient.UpdateBadgeAsync(new UpdateBadgeRequest
                {
                    Id = createResponse.Badge.Id,
                    Name = createResponse.Badge.Name,
                    Description = createResponse.Badge.Description,
                    ImageUrl = createResponse.Badge.ImageUrl,
                    MiniImageUrl = createResponse.Badge.MiniImageUrl,
                    IsActive = false
                });
            }

            return Results.Ok(new
            {
                id = createResponse.Badge.Id,
                name = createResponse.Badge.Name,
                description = createResponse.Badge.Description,
                imageUrl = createResponse.Badge.ImageUrl,
                miniImageUrl = createResponse.Badge.MiniImageUrl,
                isActive = createResponse.Badge.IsActive,
                createdDate = createResponse.Badge.CreatedDate?.ToDateTime()
            });
        })
        .WithName("CreateBadge");

        // PUT /api/badges/{id} — обновить бейдж
        group.MapPut("/{id:int}", async (
            int id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient,
            FilesServerApi.FilesServerApiClient filesClient) =>
        {
            string name, description, imageUrl, miniImageUrl;
            bool isActive;

            var body = default(UpdateBadgeBody);
            IFormFile? imageFile = null;
            IFormFile? miniImageFile = null;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync();
                name = form["name"].FirstOrDefault() ?? string.Empty;
                description = form["description"].FirstOrDefault() ?? string.Empty;
                imageUrl = form["imageUrl"].FirstOrDefault() ?? string.Empty;
                miniImageUrl = form["miniImageUrl"].FirstOrDefault() ?? string.Empty;
                isActive = bool.TryParse(form["isActive"].FirstOrDefault(), out var a) && a;
                imageFile = form.Files.GetFile("image");
                miniImageFile = form.Files.GetFile("miniImage");
            }
            else
            {
                body = await request.ReadFromJsonAsync<UpdateBadgeBody>();
                if (body == null)
                    return Results.BadRequest("Invalid request body");

                name = body.Name ?? string.Empty;
                description = body.Description ?? string.Empty;
                imageUrl = body.ImageUrl ?? string.Empty;
                miniImageUrl = body.MiniImageUrl ?? string.Empty;
                isActive = body.IsActive;
            }

            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Name is required");

            var existing = (await usersClient.GetAllBadgesAsync(new GetAllBadgesRequest { IncludeInactive = true }))
                .Badges.FirstOrDefault(badge => badge.Id == id);
            if (existing is null)
                return Results.NotFound("Badge not found");

            string? uploadedImageUrl = null;
            if (imageFile is not null)
            {
                try
                {
                    uploadedImageUrl = (await UploadBadgeImageAsync(imageFile, BadgeImageKind.Full, filesClient)).PermanentUrl;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
                {
                    return InvalidBadgeImageResponse(ex);
                }
            }
            imageUrl = ResolveBadgeImageUrl(uploadedImageUrl, imageUrl, existing.ImageUrl);

            string? uploadedMiniImageUrl = null;
            if (miniImageFile is not null)
            {
                try
                {
                    uploadedMiniImageUrl = (await UploadBadgeImageAsync(miniImageFile, BadgeImageKind.Mini, filesClient)).PermanentUrl;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
                {
                    return InvalidBadgeImageResponse(ex);
                }
            }
            miniImageUrl = ResolveBadgeImageUrl(uploadedMiniImageUrl, miniImageUrl, existing.MiniImageUrl);

            var response = await usersClient.UpdateBadgeAsync(new UpdateBadgeRequest
            {
                Id = id,
                Name = name,
                Description = description,
                ImageUrl = imageUrl,
                MiniImageUrl = miniImageUrl,
                IsActive = isActive
            });

            return Results.Ok(new
            {
                id = response.Badge.Id,
                name = response.Badge.Name,
                description = response.Badge.Description,
                imageUrl = response.Badge.ImageUrl,
                miniImageUrl = response.Badge.MiniImageUrl,
                isActive = response.Badge.IsActive,
                createdDate = response.Badge.CreatedDate?.ToDateTime()
            });
        })
        .WithName("UpdateBadge")
        .RequireStepUp(StepUpActions.BadgesUpdate, context =>
            "badgeId=" + context.Request.RouteValues["id"]);

        // DELETE /api/badges/{id} — удалить бейдж
        group.MapDelete("/{id:int}", async (
            int id,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
            var response = await usersClient.DeleteBadgeAsync(new DeleteBadgeRequest
            {
                Id = id
            });

            return Results.Ok(new { success = response.Success });
        })
        .WithName("DeleteBadge")
        .RequireStepUp(StepUpActions.BadgesDelete, context =>
            "badgeId=" + context.Request.RouteValues["id"]);
    }

    private static async Task<UploadBadgeImageResponse> UploadBadgeImageAsync(
        IFormFile imageFile,
        BadgeImageKind kind,
        FilesServerApi.FilesServerApiClient filesClient)
    {
        using var stream = new MemoryStream();
        await imageFile.CopyToAsync(stream);

        return await filesClient.UploadBadgeImageAsync(new UploadBadgeImageRequest
        {
            ImageData = ByteString.CopyFrom(stream.ToArray()),
            Filename = imageFile.FileName,
            Kind = kind
        });
    }

    public static string? ValidateCreateBadgeImages(IFormFile? fullImage, IFormFile? miniImage)
    {
        return fullImage is null || miniImage is null
            ? "Для создания бейджа нужны полная картинка и миниатюра"
            : null;
    }

    public static string ResolveBadgeImageUrl(string? uploadedUrl, string? requestedUrl, string existingUrl) =>
        !string.IsNullOrWhiteSpace(uploadedUrl)
            ? uploadedUrl
            : !string.IsNullOrWhiteSpace(requestedUrl)
                ? requestedUrl
                : existingUrl;

    public static IResult InvalidBadgeImageResponse(RpcException exception) =>
        Results.BadRequest(exception.Status.Detail);

    private sealed record UpdateBadgeBody(
        string? Name,
        string? Description,
        string? ImageUrl,
        bool IsActive,
        string? MiniImageUrl = null);
}
