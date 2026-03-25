using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Shared;

using Google.Protobuf;

namespace Barkfluff.AdminPanel.Endpoints;

public static class StickersEndpoints
{
    public static void MapStickersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/stickers")
            .WithTags("Stickers");

        // GET /api/stickers/packs — список стикерпаков
        group.MapGet("/packs", async (
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var response = await filesClient.ListStickerPacksAsync(new ListStickerPacksRequest
            {
                Pagination = new PageRequest { Offset = 0, Size = 200 }
            });

            // Собираем cover sticker ids для batch-запроса URL обложек
            var coverStickerIds = response.Packs
                .Where(p => !string.IsNullOrEmpty(p.CoverStickerId))
                .Select(p => p.CoverStickerId)
                .Distinct()
                .ToList();

            var coverStickers = new Dictionary<string, StickerInfo>();
            if (coverStickerIds.Count > 0)
            {
                var stickersResponse = await filesClient.GetStickersAsync(new GetStickersRequest
                {
                    StickerIds = { coverStickerIds }
                });
                foreach (var s in stickersResponse.Stickers)
                    coverStickers[s.Id] = s;
            }

            var packs = response.Packs.Select(p =>
            {
                string? coverUrl = null;
                if (!string.IsNullOrEmpty(p.CoverStickerId) && coverStickers.TryGetValue(p.CoverStickerId, out var coverSticker))
                    coverUrl = !string.IsNullOrEmpty(coverSticker.PreviewUrl) ? coverSticker.PreviewUrl : coverSticker.FileUrl;

                return new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    stickerCount = p.StickerCount,
                    coverUrl,
                    createdAt = p.CreatedAt?.ToDateTime()
                };
            });

            return Results.Ok(new { packs, totalCount = response.TotalCount });
        })
        .WithName("ListStickerPacks");

        // POST /api/stickers/packs — создать стикерпак
        group.MapPost("/packs", async (
            HttpRequest request,
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

            // Создаём пак
            var createResponse = await filesClient.CreateStickerPackAsync(new CreateStickerPackRequest
            {
                CreatorUserId = 0,
                Name = name,
                Description = form["description"].FirstOrDefault() ?? string.Empty
            });

            var pack = createResponse.Pack;

            // Если есть обложка — загружаем как стикер и устанавливаем обложкой
            var imageFile = form.Files.GetFile("image");
            if (imageFile != null)
            {
                using var ms = new MemoryStream();
                await imageFile.CopyToAsync(ms);

                var uploadResponse = await filesClient.UploadStickerImageAsync(new UploadStickerImageRequest
                {
                    ImageData = ByteString.CopyFrom(ms.ToArray()),
                    Filename = imageFile.FileName
                });

                var addStickerResponse = await filesClient.AddStickerAsync(new AddStickerRequest
                {
                    PackId = pack.Id,
                    FileId = uploadResponse.FileId,
                    Emoji = "⭐"
                });

                await filesClient.UpdateStickerPackAsync(new UpdateStickerPackRequest
                {
                    PackId = pack.Id,
                    Name = pack.Name,
                    Description = pack.Description,
                    CoverStickerId = addStickerResponse.Sticker.Id
                });
            }

            return Results.Ok(new
            {
                id = pack.Id,
                name = pack.Name,
                description = pack.Description,
                stickerCount = pack.StickerCount
            });
        })
        .WithName("CreateStickerPack");

        // GET /api/stickers/packs/{id} — стикерпак с содержимым
        group.MapGet("/packs/{id}", async (
            string id,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var response = await filesClient.GetStickerPackAsync(new GetStickerPackRequest
            {
                PackId = id
            });

            var pack = response.Pack;

            // Найдём URL обложки
            string? coverUrl = null;
            if (!string.IsNullOrEmpty(pack.CoverStickerId))
            {
                var coverSticker = response.Stickers.FirstOrDefault(s => s.Id == pack.CoverStickerId);
                if (coverSticker != null)
                    coverUrl = !string.IsNullOrEmpty(coverSticker.PreviewUrl) ? coverSticker.PreviewUrl : coverSticker.FileUrl;
            }

            return Results.Ok(new
            {
                id = pack.Id,
                name = pack.Name,
                description = pack.Description,
                coverStickerId = pack.CoverStickerId,
                coverUrl,
                stickerCount = pack.StickerCount,
                createdAt = pack.CreatedAt?.ToDateTime(),
                stickers = response.Stickers.Select(s => new
                {
                    id = s.Id,
                    fileUrl = s.FileUrl,
                    previewUrl = s.PreviewUrl,
                    emoji = s.Emoji,
                    addedAt = s.AddedAt?.ToDateTime()
                })
            });
        })
        .WithName("GetStickerPack");

        // PUT /api/stickers/packs/{id} — обновить пак (name, description, coverStickerId)
        group.MapPut("/packs/{id}", async (
            string id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<UpdatePackBody>();
            if (body == null)
                return Results.BadRequest("Invalid request body");

            var response = await filesClient.UpdateStickerPackAsync(new UpdateStickerPackRequest
            {
                PackId = id,
                Name = body.Name ?? string.Empty,
                Description = body.Description ?? string.Empty,
                CoverStickerId = body.CoverStickerId ?? string.Empty
            });

            return Results.Ok(new
            {
                id = response.Pack.Id,
                name = response.Pack.Name,
                description = response.Pack.Description,
                coverStickerId = response.Pack.CoverStickerId,
                stickerCount = response.Pack.StickerCount
            });
        })
        .WithName("UpdateStickerPack");

        // PUT /api/stickers/packs/{id}/cover — сменить обложку (multipart: image)
        group.MapPut("/packs/{id}/cover", async (
            string id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();
            var imageFile = form.Files.GetFile("image");
            if (imageFile == null)
                return Results.BadRequest("Image file is required");

            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);

            var uploadResponse = await filesClient.UploadStickerImageAsync(new UploadStickerImageRequest
            {
                ImageData = ByteString.CopyFrom(ms.ToArray()),
                Filename = imageFile.FileName
            });

            var addStickerResponse = await filesClient.AddStickerAsync(new AddStickerRequest
            {
                PackId = id,
                FileId = uploadResponse.FileId,
                Emoji = "⭐"
            });

            // Получаем текущий пак для сохранения name/description
            var packResponse = await filesClient.GetStickerPackAsync(new GetStickerPackRequest { PackId = id });

            await filesClient.UpdateStickerPackAsync(new UpdateStickerPackRequest
            {
                PackId = id,
                Name = packResponse.Pack.Name,
                Description = packResponse.Pack.Description,
                CoverStickerId = addStickerResponse.Sticker.Id
            });

            return Results.Ok(new { coverStickerId = addStickerResponse.Sticker.Id, fileUrl = addStickerResponse.Sticker.FileUrl });
        })
        .WithName("UpdateStickerPackCover");

        // DELETE /api/stickers/packs/{id} — удалить стикерпак
        group.MapDelete("/packs/{id}", async (
            string id,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            await filesClient.DeleteStickerPackAsync(new DeleteStickerPackRequest { PackId = id });
            return Results.Ok(new { success = true });
        })
        .WithName("DeleteStickerPack");

        // POST /api/stickers/packs/{id}/stickers — добавить стикер в пак
        group.MapPost("/packs/{id}/stickers", async (
            string id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();

            var emoji = form["emoji"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(emoji))
                return Results.BadRequest("Emoji is required");

            var imageFile = form.Files.GetFile("image");
            if (imageFile == null)
                return Results.BadRequest("Image file is required");

            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);

            var uploadResponse = await filesClient.UploadStickerImageAsync(new UploadStickerImageRequest
            {
                ImageData = ByteString.CopyFrom(ms.ToArray()),
                Filename = imageFile.FileName
            });

            var addResponse = await filesClient.AddStickerAsync(new AddStickerRequest
            {
                PackId = id,
                FileId = uploadResponse.FileId,
                Emoji = emoji
            });

            var sticker = addResponse.Sticker;
            return Results.Ok(new
            {
                id = sticker.Id,
                fileUrl = sticker.FileUrl,
                previewUrl = sticker.PreviewUrl,
                emoji = sticker.Emoji,
                addedAt = sticker.AddedAt?.ToDateTime()
            });
        })
        .WithName("AddSticker");

        // PUT /api/stickers/{stickerId} — обновить emoji стикера
        group.MapPut("/{stickerId}", async (
            string stickerId,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<UpdateStickerBody>();
            if (body == null || string.IsNullOrWhiteSpace(body.Emoji))
                return Results.BadRequest("Emoji is required");

            var response = await filesClient.UpdateStickerAsync(new UpdateStickerRequest
            {
                StickerId = stickerId,
                Emoji = body.Emoji
            });

            return Results.Ok(new
            {
                id = response.Sticker.Id,
                emoji = response.Sticker.Emoji
            });
        })
        .WithName("UpdateSticker");

        // DELETE /api/stickers/{stickerId} — удалить стикер
        group.MapDelete("/{stickerId}", async (
            string stickerId,
            FilesServerApi.FilesServerApiClient filesClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            await filesClient.RemoveStickerAsync(new RemoveStickerRequest { StickerId = stickerId });
            return Results.Ok(new { success = true });
        })
        .WithName("RemoveSticker");
    }

    private sealed record UpdatePackBody(string? Name, string? Description, string? CoverStickerId);
    private sealed record UpdateStickerBody(string? Emoji);
}
