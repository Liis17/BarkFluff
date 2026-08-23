using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;

namespace Barkfluff.AdminPanel.Endpoints;

public static class FilesEndpoints
{
    public static void MapFilesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/files")
            .WithTags("Files");

        // GET /api/files/{fileId}
        group.MapGet("/{fileId}", async (
            string fileId,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
            GetFileDataResponse fileResponse;
            try
            {
                fileResponse = await filesClient.GetFileDataAsync(new GetFileDataRequest { FileId = fileId });
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return Results.NotFound();
            }

            var info = fileResponse.FileInfo;

            var uploaders = Enumerable.Empty<object>();
            if (info.Uploaders.Count > 0)
            {
                var usersResponse = await usersClient.ListByIdsAsync(
                    new ListByIdsRequest { Ids = { info.Uploaders } });

                uploaders = usersResponse.Users.Select(u => new
                {
                    id = u.Id,
                    firstName = u.FirstName,
                    lastName = u.LastName,
                    username = u.Username,
                    profilePicturePreview = u.ProfilePicturePreview
                });
            }

            return Results.Ok(new
            {
                fileId = info.Id,
                fileName = info.FileName,
                fileSize = info.FileSize,
                type = info.Type.ToString(),
                previewUrl = info.PreviewUrl,
                imageWidth = info.ImageWidth,
                imageHeight = info.ImageHeight,
                createdAt = info.CreatedAt?.ToDateTime(),
                uploadedAt = info.UploadedAt?.ToDateTime(),
                uploaders
            });
        })
        .WithName("GetFileDetails");
    }
}
