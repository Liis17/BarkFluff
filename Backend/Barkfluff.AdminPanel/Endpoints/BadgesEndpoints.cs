using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

using Google.Protobuf;

namespace Barkfluff.AdminPanel.Endpoints;

public static class BadgesEndpoints
{
    public static void MapBadgesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/badges")
            .WithTags("Badges");

        // GET /api/badges — все бейджи (включая неактивные)
        group.MapGet("/", async (
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

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
                isActive = b.IsActive,
                createdDate = b.CreatedDate?.ToDateTime()
            });

            return Results.Ok(badges);
        })
        .WithName("GetAllBadges");

        // POST /api/badges — создать новый бейдж (multipart: name, description, isActive, image)
        group.MapPost("/", async (
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();

            var name = form["name"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Name is required");

            var description = form["description"].FirstOrDefault() ?? string.Empty;
            var isActive = bool.TryParse(form["isActive"].FirstOrDefault(), out var active) && active;

            var imageFile = form.Files.GetFile("image");
            if (imageFile == null)
                return Results.BadRequest("Image file is required");

            // Загружаем картинку в Files-сервис
            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);
            var imageBytes = ms.ToArray();

            var uploadResponse = await filesClient.UploadBadgeImageAsync(new UploadBadgeImageRequest
            {
                ImageData = ByteString.CopyFrom(imageBytes),
                Filename = imageFile.FileName
            });

            // Создаём бейдж в Users-сервисе
            var createResponse = await usersClient.CreateBadgeAsync(new CreateBadgeRequest
            {
                Name = name,
                Description = description,
                ImageUrl = uploadResponse.PermanentUrl
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
                    IsActive = false
                });
            }

            return Results.Ok(new
            {
                id = createResponse.Badge.Id,
                name = createResponse.Badge.Name,
                description = createResponse.Badge.Description,
                imageUrl = createResponse.Badge.ImageUrl,
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
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            string name, description, imageUrl;
            bool isActive;

            if (request.HasFormContentType)
            {
                // Форма с возможной заменой картинки
                var form = await request.ReadFormAsync();
                name = form["name"].FirstOrDefault() ?? string.Empty;
                description = form["description"].FirstOrDefault() ?? string.Empty;
                imageUrl = form["imageUrl"].FirstOrDefault() ?? string.Empty;
                isActive = bool.TryParse(form["isActive"].FirstOrDefault(), out var a) && a;

                var imageFile = form.Files.GetFile("image");
                if (imageFile != null)
                {
                    using var ms = new MemoryStream();
                    await imageFile.CopyToAsync(ms);
                    var uploadResponse = await filesClient.UploadBadgeImageAsync(new UploadBadgeImageRequest
                    {
                        ImageData = ByteString.CopyFrom(ms.ToArray()),
                        Filename = imageFile.FileName
                    });
                    imageUrl = uploadResponse.PermanentUrl;
                }
            }
            else
            {
                // JSON body
                var body = await request.ReadFromJsonAsync<UpdateBadgeBody>();
                if (body == null)
                    return Results.BadRequest("Invalid request body");

                name = body.Name ?? string.Empty;
                description = body.Description ?? string.Empty;
                imageUrl = body.ImageUrl ?? string.Empty;
                isActive = body.IsActive;
            }

            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest("Name is required");

            var response = await usersClient.UpdateBadgeAsync(new UpdateBadgeRequest
            {
                Id = id,
                Name = name,
                Description = description,
                ImageUrl = imageUrl,
                IsActive = isActive
            });

            return Results.Ok(new
            {
                id = response.Badge.Id,
                name = response.Badge.Name,
                description = response.Badge.Description,
                imageUrl = response.Badge.ImageUrl,
                isActive = response.Badge.IsActive,
                createdDate = response.Badge.CreatedDate?.ToDateTime()
            });
        })
        .WithName("UpdateBadge");

        // DELETE /api/badges/{id} — удалить бейдж
        group.MapDelete("/{id:int}", async (
            int id,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var response = await usersClient.DeleteBadgeAsync(new DeleteBadgeRequest
            {
                Id = id
            });

            return Results.Ok(new { success = response.Success });
        })
        .WithName("DeleteBadge");
    }

    private sealed record UpdateBadgeBody(string? Name, string? Description, string? ImageUrl, bool IsActive);
}
