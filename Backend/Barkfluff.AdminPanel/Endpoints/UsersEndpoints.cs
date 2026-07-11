using Barkfluff.AdminPanel.Models;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;

using Google.Protobuf;

namespace Barkfluff.AdminPanel.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

        group.AddEndpointFilter(async (filterContext, next) =>
        {
            if (filterContext.HttpContext.Items["AuthToken"] is not AuthToken ||
                !long.TryParse(filterContext.HttpContext.Request.RouteValues["id"]?.ToString(), out var userId))
            {
                return await next(filterContext);
            }

            try
            {
                var usersClient = filterContext.HttpContext.RequestServices
                    .GetRequiredService<UsersServerApi.UsersServerApiClient>();
                var response = await usersClient.GetByIdAsync(new GetByIdRequest { UserId = userId });

                if (response.User is null || response.User.IsBot)
                    return Results.NotFound();
            }
            catch (Grpc.Core.RpcException)
            {
                return Results.NotFound();
            }

            return await next(filterContext);
        });

        // GET /api/users?query=&offset=0&size=50
        group.MapGet("/", async (
            string? query,
            int? offset,
            int? size,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var response = await usersClient.SearchUsersServerAsync(new SearchUsersServerRequest
            {
                Query = query ?? string.Empty,
                Offset = offset ?? 0,
                Size = size ?? 50
            });

            var users = response.Users.Select(u => new
            {
                id = u.Id,
                firstName = u.FirstName,
                lastName = u.LastName,
                username = u.Username,
                profilePicture = u.ProfilePicture,
                profilePicturePreview = u.ProfilePicturePreview,
                registrationDate = u.RegistrationDate?.ToDateTime(),
                bio = u.Bio,
                storageLimitGb = u.StorageLimitGb,
                badges = u.Badges.Select(b => new
                {
                    badge = new
                    {
                        id = b.Badge.Id,
                        name = b.Badge.Name,
                        description = b.Badge.Description,
                        imageUrl = b.Badge.ImageUrl
                    },
                    priority = b.Priority
                })
            });

            return Results.Ok(new { users, totalCount = response.TotalCount });
        })
        .WithName("SearchUsers");

        // GET /api/users/{id}
        group.MapGet("/{id:long}", async (
            long id,
            UsersServerApi.UsersServerApiClient usersClient,
            FilesServerApi.FilesServerApiClient filesClient,
            IdentityServerApi.IdentityServerApiClient identityClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            // Параллельные вызовы (SearchUsersServer вместо GetById — включает бейджи)
            var userSearchTask = SafeCall(() => usersClient.SearchUsersServerAsync(new SearchUsersServerRequest
            {
                Query = id.ToString(),
                Offset = 0,
                Size = 1
            }));
            var contactsTask = SafeCall(() => usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = id }));
            var devicesTask = SafeCall(() => usersClient.GetUserDevicesAsync(new GetUserDevicesRequest { UserId = id }));
            var storageTask = SafeCall(() => filesClient.GetUserStorageInfoServerAsync(new GetUserStorageInfoServerRequest { UserId = id }));
            var otpTask = SafeCall(() => identityClient.ListOtpVerificationServerAsync(new ListOtpVerificationServerRequest { UserId = id }));
            var sessionsTask = SafeCall(() => identityClient.GetActiveSessionsServerAsync(new GetActiveSessionsServerRequest { UserId = id }));
            var posterTask = SafeCall(() => usersClient.GetProfilePosterServerAsync(new GetProfilePosterServerRequest { UserId = id }));

            await Task.WhenAll(userSearchTask, contactsTask, devicesTask, storageTask, otpTask, sessionsTask, posterTask);

            var userSearch = userSearchTask.Result;
            var contacts = contactsTask.Result;
            var devices = devicesTask.Result;
            var storage = storageTask.Result;
            var otp = otpTask.Result;
            var sessions = sessionsTask.Result;
            var poster = posterTask.Result;

            var userProto = userSearch?.Users.FirstOrDefault();
            if (userProto is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                profile = new
                {
                    id = userProto.Id,
                    firstName = userProto.FirstName,
                    lastName = userProto.LastName,
                    username = userProto.Username,
                    profilePicture = userProto.ProfilePicture,
                    profilePicturePreview = userProto.ProfilePicturePreview,
                    profilePosterUrl = poster?.PosterUrl ?? string.Empty,
                    bio = userProto.Bio,
                    registrationDate = userProto.RegistrationDate?.ToDateTime(),
                    storageLimitGb = userProto.StorageLimitGb,
                    badges = userProto.Badges.Select(b => new
                    {
                        badge = new
                        {
                            id = b.Badge.Id,
                            name = b.Badge.Name,
                            description = b.Badge.Description,
                            imageUrl = b.Badge.ImageUrl
                        },
                        priority = b.Priority,
                        assignedDate = b.AssignedDate?.ToDateTime()
                    })
                },
                contacts = contacts != null ? new
                {
                    email = contacts.Contact?.Email
                } : null,
                devices = devices?.Devices.Select(d => new
                {
                    deviceId = d.DeviceId,
                    originalName = d.OriginalName,
                    customName = d.CustomName,
                    operationSystem = d.OperationSystem,
                    appName = d.AppName,
                    location = d.Location,
                    authorizedAt = d.AuthorizedAt?.ToDateTime()
                }),
                storage = storage != null ? new
                {
                    totalUsedStorage = storage.TotalUsedStorage,
                    storageLimit = storage.StorageLimit,
                    storageByTypes = storage.StorageByTypes.Select(s => new
                    {
                        fileType = (int)s.FileType,
                        fileTypeName = s.FileType.ToString(),
                        usedStorage = s.UsedStorage
                    })
                } : null,
                twoFactor = otp != null ? new
                {
                    authenticatorEnabled = otp.AuthenticatorEnabled,
                    emailEnabled = otp.EmailEnabled
                } : null,
                sessions = sessions?.Sessions.Select(s => new
                {
                    id = s.Id,
                    deviceId = s.DeviceId,
                    originalName = s.OriginalName,
                    customName = s.CustomName,
                    appName = s.AppName,
                    operationSystem = s.OperationSystem,
                    location = s.Location,
                    createdAt = s.CreatedAt?.ToDateTime(),
                    expirationAt = s.ExpirationAt?.ToDateTime()
                })
            });
        })
        .WithName("GetUserDetails");

        // POST /api/users/{id}/badges
        group.MapPost("/{id:long}/badges", async (
            long id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<AssignBadgeBody>();
            if (body is null)
                return Results.BadRequest("Invalid request body");

            var response = await usersClient.AssignUserBadgeAsync(new AssignUserBadgeRequest
            {
                UserId = id,
                BadgeId = body.BadgeId,
                Priority = body.Priority ?? 1000
            });

            return Results.Ok(new
            {
                badge = new
                {
                    id = response.UserBadge.Badge.Id,
                    name = response.UserBadge.Badge.Name,
                    description = response.UserBadge.Badge.Description,
                    imageUrl = response.UserBadge.Badge.ImageUrl
                },
                priority = response.UserBadge.Priority,
                assignedDate = response.UserBadge.AssignedDate?.ToDateTime()
            });
        })
        .WithName("AssignUserBadge");

        // DELETE /api/users/{id}/badges/{badgeId}
        group.MapDelete("/{id:long}/badges/{badgeId:int}", async (
            long id,
            int badgeId,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var response = await usersClient.RemoveUserBadgeAsync(new RemoveUserBadgeRequest
            {
                UserId = id,
                BadgeId = badgeId
            });

            return Results.Ok(new { success = response.Success });
        })
        .WithName("RemoveUserBadge");

        // PUT /api/users/{id}/storage-limit
        group.MapPut("/{id:long}/storage-limit", async (
            long id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<UpdateStorageLimitBody>();
            if (body is null || body.StorageLimitGb < 1 || body.StorageLimitGb > 250)
                return Results.BadRequest("storageLimitGb must be between 1 and 250");

            var response = await usersClient.UpdateStorageLimitAsync(new UpdateStorageLimitRequest
            {
                UserId = id,
                StorageLimitGb = body.StorageLimitGb
            });

            return Results.Ok(new
            {
                id = response.User.Id,
                storageLimitGb = response.User.StorageLimitGb
            });
        })
        .WithName("UpdateStorageLimit");

        // POST /api/users/{id}/2fa/disable
        group.MapPost("/{id:long}/2fa/disable", async (
            long id,
            HttpRequest request,
            IdentityServerApi.IdentityServerApiClient identityClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<DisableOtpBody>();
            if (body is null)
                return Results.BadRequest("Invalid request body");

            await identityClient.DisableOtpVerificationServerAsync(new DisableOtpVerificationServerRequest
            {
                UserId = id,
                OtpType = (OtpTypeId)body.OtpType
            });

            return Results.Ok(new { success = true });
        })
        .WithName("DisableUserOtp");

        // POST /api/users/{id}/avatar
        group.MapPost("/{id:long}/avatar", async (
            long id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("avatar");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No avatar file provided");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            // Загружаем в Files сервис
            var uploadResponse = await filesClient.UploadAvatarServerAsync(new UploadAvatarServerRequest
            {
                ImageData = ByteString.CopyFrom(ms.ToArray()),
                Filename = file.FileName,
                UserId = id
            });

            // Обновляем аватарку пользователя в Users сервисе
            await usersClient.SetProfilePictureServerAsync(new SetProfilePictureServerRequest
            {
                UserId = id,
                ProfilePictureUrl = uploadResponse.FileUrl,
                ProfilePicturePreviewUrl = uploadResponse.PreviewUrl
            });

            return Results.Ok(new
            {
                fileUrl = uploadResponse.FileUrl,
                previewUrl = uploadResponse.PreviewUrl,
                fileId = uploadResponse.FileId
            });
        })
        .DisableAntiforgery()
        .WithName("UploadUserAvatar");

        // PUT /api/users/{id}/profile
        group.MapPut("/{id:long}/profile", async (
            long id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<UpdateProfileBody>();
            if (body is null)
                return Results.BadRequest("Invalid request body");

            await usersClient.UpdateProfileServerAsync(new UpdateProfileServerRequest
            {
                UserId = id,
                FirstName = body.FirstName ?? string.Empty,
                LastName = body.LastName ?? string.Empty,
                Bio = body.Bio ?? string.Empty,
                Username = body.Username ?? string.Empty
            });

            return Results.Ok(new { success = true });
        })
        .WithName("UpdateUserProfile");

        // POST /api/users/{id}/password
        group.MapPost("/{id:long}/password", async (
            long id,
            HttpRequest request,
            IdentityServerApi.IdentityServerApiClient identityClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<ChangePasswordBody>();
            if (body is null || string.IsNullOrWhiteSpace(body.NewPassword))
                return Results.BadRequest("newPassword is required");

            if (body.NewPassword.Length < 6)
                return Results.BadRequest("newPassword must be at least 6 characters");

            await identityClient.ForceSetPasswordServerAsync(new ForceSetPasswordServerRequest
            {
                UserId = id,
                NewPassword = body.NewPassword
            });

            return Results.Ok(new { success = true });
        })
        .WithName("ForceChangeUserPassword");

        // POST /api/users/{id}/poster
        group.MapPost("/{id:long}/poster", async (
            long id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("poster");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No poster file provided");

            if (file.Length > 15 * 1024 * 1024)
                return Results.BadRequest("File too large (max 15 MB)");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var uploadResponse = await filesClient.UploadPosterServerAsync(new UploadPosterServerRequest
            {
                ImageData = ByteString.CopyFrom(ms.ToArray()),
                Filename = file.FileName,
                UserId = id
            });

            await usersClient.SetProfilePosterServerAsync(new SetProfilePosterServerRequest
            {
                UserId = id,
                PosterFileId = uploadResponse.FileId
            });

            return Results.Ok(new
            {
                posterUrl = uploadResponse.FileUrl,
                fileId = uploadResponse.FileId
            });
        })
        .DisableAntiforgery()
        .WithName("UploadUserPoster");

        // DELETE /api/users/{id}/sessions/{deviceId}
        group.MapDelete("/{id:long}/sessions/{deviceId}", async (
            long id,
            string deviceId,
            IdentityServerApi.IdentityServerApiClient identityClient,
            HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            await identityClient.RemoveActiveSessionServerAsync(new RemoveActiveSessionServerRequest
            {
                UserId = id,
                DeviceId = deviceId
            });

            return Results.Ok(new { success = true });
        })
        .WithName("RemoveUserSession");
    }

    private static async Task<T?> SafeCall<T>(Func<Grpc.Core.AsyncUnaryCall<T>> call) where T : class
    {
        try
        {
            return await call();
        }
        catch
        {
            return null;
        }
    }

    private sealed record AssignBadgeBody(int BadgeId, int? Priority);
    private sealed record UpdateStorageLimitBody(int StorageLimitGb);
    private sealed record DisableOtpBody(int OtpType);
    private sealed record UpdateProfileBody(string? FirstName, string? LastName, string? Bio, string? Username);
    private sealed record ChangePasswordBody(string? NewPassword);
}
