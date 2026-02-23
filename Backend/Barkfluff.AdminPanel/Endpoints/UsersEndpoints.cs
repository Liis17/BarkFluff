using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using Barkfluff.AdminPanel.Models;

namespace Barkfluff.AdminPanel.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

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

            // Параллельные вызовы
            var userTask = SafeCall(() => usersClient.GetByIdAsync(new GetByIdRequest { UserId = id }));
            var contactsTask = SafeCall(() => usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = id }));
            var devicesTask = SafeCall(() => usersClient.GetUserDevicesAsync(new GetUserDevicesRequest { UserId = id }));
            var storageTask = SafeCall(() => filesClient.GetUserStorageInfoServerAsync(new GetUserStorageInfoServerRequest { UserId = id }));
            var otpTask = SafeCall(() => identityClient.ListOtpVerificationServerAsync(new ListOtpVerificationServerRequest { UserId = id }));
            var sessionsTask = SafeCall(() => identityClient.GetActiveSessionsServerAsync(new GetActiveSessionsServerRequest { UserId = id }));

            await Task.WhenAll(userTask, contactsTask, devicesTask, storageTask, otpTask, sessionsTask);

            var user = userTask.Result;
            var contacts = contactsTask.Result;
            var devices = devicesTask.Result;
            var storage = storageTask.Result;
            var otp = otpTask.Result;
            var sessions = sessionsTask.Result;

            if (user is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                profile = new
                {
                    id = user.User.Id,
                    firstName = user.User.FirstName,
                    lastName = user.User.LastName,
                    username = user.User.Username,
                    profilePicture = user.User.ProfilePicture,
                    profilePicturePreview = user.User.ProfilePicturePreview,
                    bio = user.User.Bio,
                    registrationDate = user.User.RegistrationDate?.ToDateTime(),
                    storageLimitGb = user.User.StorageLimitGb,
                    badges = user.User.Badges.Select(b => new
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
}
