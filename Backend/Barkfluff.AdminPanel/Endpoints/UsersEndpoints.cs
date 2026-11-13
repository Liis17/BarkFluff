using Barkfluff.AdminPanel.Middleware;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;

using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;

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

        // GET /api/users?query=&status=all|blocked|pending_deletion&offset=0&size=50
        group.MapGet("/", async (
            string? query,
            string? status,
            int? offset,
            int? size,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
            var statusFilter = status?.Trim().ToLowerInvariant();
            if (statusFilter is not (null or "" or "all" or "blocked" or "pending_deletion"))
                return Results.BadRequest(new { error = "status must be all, blocked, or pending_deletion" });

            var request = new SearchUsersServerRequest
            {
                Query = query ?? string.Empty,
                Offset = offset ?? 0,
                Size = size ?? 50
            };
            if (statusFilter == "blocked")
                request.StatusFilter = AccountStatus.Blocked;
            else if (statusFilter == "pending_deletion")
                request.StatusFilter = AccountStatus.PendingDeletion;

            var response = await usersClient.SearchUsersServerAsync(request);
            var restrictions = response.Results
                .Where(result => result.User is not null)
                .ToDictionary(result => result.User.Id, result => result.Restriction);

            var users = response.Users.Select(u =>
            {
                restrictions.TryGetValue(u.Id, out var restriction);
                return new
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
                    accountStatus = u.AccountStatus.ToString(),
                    restrictionReason = restriction?.Reason ?? string.Empty,
                    restrictedAt = restriction?.RestrictedAt?.ToDateTime(),
                    deletionScheduledAt = restriction?.DeletionScheduledAt?.ToDateTime(),
                    badges = u.Badges.Select(b => new
                    {
                        badge = new
                        {
                            id = b.Badge.Id,
                            name = b.Badge.Name,
                            description = b.Badge.Description,
                            imageUrl = b.Badge.ImageUrl,
                            miniImageUrl = b.Badge.MiniImageUrl
                        },
                        priority = b.Priority,
                        assignedDate = b.AssignedDate?.ToDateTime(),
                        isPrimary = b.IsPrimary
                    })
                };
            });

            return Results.Ok(new { users, totalCount = response.TotalCount });
        })
        .WithName("SearchUsers")
        .RequirePermission(AdminPermissions.UsersRead);

        // GET /api/users/{id}
        group.MapGet("/{id:long}", async (
            long id,
            UsersServerApi.UsersServerApiClient usersClient,
            FilesServerApi.FilesServerApiClient filesClient,
            IdentityServerApi.IdentityServerApiClient identityClient) =>
        {
            // Параллельные вызовы (SearchUsersServer вместо GetById — включает бейджи)
            var userSearchTask = SafeCall(() => usersClient.SearchUsersServerAsync(new SearchUsersServerRequest
            {
                Query = id.ToString(),
                Offset = 0,
                Size = 1
            }));
            var contactsTask = SafeCall(() => usersClient.GetUserContactsAsync(new GetUserContactsRequest { UserId = id }));
            var restrictionTask = SafeCall(() => usersClient.GetAccountRestrictionAsync(new GetAccountRestrictionRequest { UserId = id }));
            var devicesTask = SafeCall(() => usersClient.GetUserDevicesAsync(new GetUserDevicesRequest { UserId = id }));
            var storageTask = SafeCall(() => filesClient.GetUserStorageInfoServerAsync(new GetUserStorageInfoServerRequest { UserId = id }));
            var otpTask = SafeCall(() => identityClient.ListOtpVerificationServerAsync(new ListOtpVerificationServerRequest { UserId = id }));
            var sessionsTask = SafeCall(() => identityClient.GetActiveSessionsServerAsync(new GetActiveSessionsServerRequest { UserId = id }));
            var posterTask = SafeCall(() => usersClient.GetProfilePosterServerAsync(new GetProfilePosterServerRequest { UserId = id }));

            await Task.WhenAll(userSearchTask, contactsTask, restrictionTask, devicesTask, storageTask, otpTask, sessionsTask, posterTask);

            var userSearch = userSearchTask.Result;
            var contacts = contactsTask.Result;
            var restriction = restrictionTask.Result;
            var devices = devicesTask.Result;
            var storage = storageTask.Result;
            var otp = otpTask.Result;
            var sessions = sessionsTask.Result;
            var poster = posterTask.Result;

            if (!userSearch.Available)
                return Results.Problem("Сервис пользователей временно недоступен", statusCode: StatusCodes.Status503ServiceUnavailable);

            var userResult = userSearch.Value?.Results.FirstOrDefault();
            var userProto = userResult?.User ?? userSearch.Value?.Users.FirstOrDefault();
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
                    profilePosterUrl = poster.Value?.PosterUrl ?? string.Empty,
                    bio = userProto.Bio,
                    registrationDate = userProto.RegistrationDate?.ToDateTime(),
                    storageLimitGb = userProto.StorageLimitGb,
                    accountStatus = userProto.AccountStatus.ToString(),
                    restrictionReason = userResult?.Restriction?.Reason ?? string.Empty,
                    restrictedAt = userResult?.Restriction?.RestrictedAt?.ToDateTime(),
                    deletionScheduledAt = userResult?.Restriction?.DeletionScheduledAt?.ToDateTime(),
                    badges = userProto.Badges.Select(b => new
                    {
                        badge = new
                        {
                            id = b.Badge.Id,
                            name = b.Badge.Name,
                            description = b.Badge.Description,
                            imageUrl = b.Badge.ImageUrl,
                            miniImageUrl = b.Badge.MiniImageUrl
                        },
                        priority = b.Priority,
                        assignedDate = b.AssignedDate?.ToDateTime(),
                        isPrimary = b.IsPrimary
                    })
                },
                contacts = contacts.Value != null || restriction.Value != null ? new
                {
                    email = !string.IsNullOrEmpty(restriction.Value?.Email)
                        ? restriction.Value.Email
                        : contacts.Value?.Contact?.Email
                } : null,
                devices = devices.Value?.Devices.Select(d => new
                {
                    deviceId = d.DeviceId,
                    originalName = d.OriginalName,
                    customName = d.CustomName,
                    operationSystem = d.OperationSystem,
                    appName = d.AppName,
                    location = d.Location,
                    authorizedAt = d.AuthorizedAt?.ToDateTime()
                }),
                storage = storage.Value != null ? new
                {
                    totalUsedStorage = storage.Value.TotalUsedStorage,
                    storageLimit = storage.Value.StorageLimit,
                    storageByTypes = storage.Value.StorageByTypes.Select(s => new
                    {
                        fileType = (int)s.FileType,
                        fileTypeName = s.FileType.ToString(),
                        usedStorage = s.UsedStorage
                    })
                } : null,
                twoFactor = otp.Value != null ? new
                {
                    authenticatorEnabled = otp.Value.AuthenticatorEnabled,
                    emailEnabled = otp.Value.EmailEnabled
                } : null,
                sessions = sessions.Value?.Sessions.Select(s => new
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
                }),
                availability = new
                {
                    contacts = contacts.Available,
                    devices = devices.Available,
                    storage = storage.Available,
                    twoFactor = otp.Available,
                    sessions = sessions.Available,
                    poster = poster.Available
                }
            });
        })
        .WithName("GetUserDetails")
        .RequirePermission(AdminPermissions.UsersRead);

        // Account restriction changes are orchestrated by Identity; each action is bound to its step-up parameters.
        group.MapPost("/{id:long}/block", (long id, [FromBody] AccountRestrictionBody? body, IdentityServerApi.IdentityServerApiClient identityClient) =>
                SetAccountRestriction(identityClient, id, AccountRestrictionAction.Block, body))
            .WithName("BlockUserAccount")
            .RequirePermission(AdminPermissions.UsersBlock)
            .RequireAdminReasonFromArguments(context => context.Arguments.OfType<AccountRestrictionBody>().FirstOrDefault()?.Reason)
            .RequireStepUpFromArguments(StepUpActions.UsersBlock, context => RestrictionStepUpParameters(context, "BLOCK"));

        group.MapPost("/{id:long}/unblock", (long id, [FromBody] AccountRestrictionBody? body, IdentityServerApi.IdentityServerApiClient identityClient) =>
                SetAccountRestriction(identityClient, id, AccountRestrictionAction.Unblock, body))
            .WithName("UnblockUserAccount")
            .RequirePermission(AdminPermissions.UsersBlock)
            .RequireAdminReasonFromArguments(context => context.Arguments.OfType<AccountRestrictionBody>().FirstOrDefault()?.Reason)
            .RequireStepUpFromArguments(StepUpActions.UsersUnblock, context => RestrictionStepUpParameters(context, "UNBLOCK"));

        group.MapPost("/{id:long}/deletion", (long id, [FromBody] AccountRestrictionBody? body, IdentityServerApi.IdentityServerApiClient identityClient) =>
                SetAccountRestriction(identityClient, id, AccountRestrictionAction.ScheduleDeletion, body))
            .WithName("ScheduleUserDeletion")
            .RequirePermission(AdminPermissions.UsersDelete)
            .RequireAdminReasonFromArguments(context => context.Arguments.OfType<AccountRestrictionBody>().FirstOrDefault()?.Reason)
            .RequireStepUpFromArguments(StepUpActions.UsersDeletionSchedule, context => RestrictionStepUpParameters(context, "SCHEDULE_DELETION"));

        group.MapDelete("/{id:long}/deletion", (long id, [FromBody] AccountRestrictionBody? body, IdentityServerApi.IdentityServerApiClient identityClient) =>
                SetAccountRestriction(identityClient, id, AccountRestrictionAction.CancelDeletion, body))
            .WithName("CancelUserDeletion")
            .RequirePermission(AdminPermissions.UsersDelete)
            .RequireAdminReasonFromArguments(context => context.Arguments.OfType<AccountRestrictionBody>().FirstOrDefault()?.Reason)
            .RequireStepUpFromArguments(StepUpActions.UsersDeletionCancel, context => RestrictionStepUpParameters(context, "CANCEL_DELETION"));

        // POST /api/users/{id}/badges
        group.MapPost("/{id:long}/badges", async (
            long id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
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
                    imageUrl = response.UserBadge.Badge.ImageUrl,
                    miniImageUrl = response.UserBadge.Badge.MiniImageUrl
                },
                priority = response.UserBadge.Priority,
                assignedDate = response.UserBadge.AssignedDate?.ToDateTime(),
                isPrimary = response.UserBadge.IsPrimary
            });
        })
        .WithName("AssignUserBadge")
        .RequirePermission(AdminPermissions.BadgesManage)
        .RequireStepUp(StepUpActions.UserBadgeAssign, context =>
            "userId=" + context.Request.RouteValues["id"]);

        // DELETE /api/users/{id}/badges/{badgeId}
        group.MapDelete("/{id:long}/badges/{badgeId:int}", async (
            long id,
            int badgeId,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
            var response = await usersClient.RemoveUserBadgeAsync(new RemoveUserBadgeRequest
            {
                UserId = id,
                BadgeId = badgeId
            });

            return Results.Ok(new { success = response.Success });
        })
        .WithName("RemoveUserBadge")
        .RequirePermission(AdminPermissions.BadgesManage)
        .RequireStepUp(StepUpActions.UserBadgeRemove, context =>
            "userId=" + context.Request.RouteValues["id"] + ";badgeId=" + context.Request.RouteValues["badgeId"]);

        // PUT /api/users/{id}/storage-limit
        group.MapPut("/{id:long}/storage-limit", async (
            long id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
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
        .WithName("UpdateStorageLimit")
        .RequirePermission(AdminPermissions.UsersRead);

        // POST /api/users/{id}/2fa/disable
        group.MapPost("/{id:long}/2fa/disable", async (
            long id,
            DisableOtpBody? body,
            IdentityServerApi.IdentityServerApiClient identityClient) =>
        {
            if (body is null)
                return Results.BadRequest("Invalid request body");

            await identityClient.DisableOtpVerificationServerAsync(new DisableOtpVerificationServerRequest
            {
                UserId = id,
                OtpType = (OtpTypeId)body.OtpType
            });

            return Results.Ok(new { success = true });
        })
        .WithName("DisableUserOtp")
        .RequirePermission(AdminPermissions.Users2FaDisable)
        .RequireAdminReasonFromArguments(context =>
            context.Arguments.OfType<DisableOtpBody>().FirstOrDefault()?.Reason)
        .RequireStepUpFromArguments(StepUpActions.Users2FaDisable, context =>
        {
            var userId = context.HttpContext.Request.RouteValues["id"];
            var body = context.Arguments.OfType<DisableOtpBody>().FirstOrDefault();
            return $"userId={userId};otpType={body?.OtpType};reason={body?.Reason?.Trim()}";
        });

        // POST /api/users/{id}/avatar
        group.MapPost("/{id:long}/avatar", async (
            long id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
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
        .WithName("UploadUserAvatar")
        .RequirePermission(AdminPermissions.UsersRead);

        // PUT /api/users/{id}/profile
        group.MapPut("/{id:long}/profile", async (
            long id,
            HttpRequest request,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
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
        .WithName("UpdateUserProfile")
        .RequirePermission(AdminPermissions.UsersRead);

        // POST /api/users/{id}/password
        group.MapPost("/{id:long}/password", async (
            long id,
            ChangePasswordBody? body,
            IdentityServerApi.IdentityServerApiClient identityClient) =>
        {
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
        .WithName("ForceChangeUserPassword")
        .RequirePermission(AdminPermissions.UsersPasswordSet)
        .RequireAdminReasonFromArguments(context =>
            context.Arguments.OfType<ChangePasswordBody>().FirstOrDefault()?.Reason)
        .RequireStepUpFromArguments(StepUpActions.UsersPasswordSet, context =>
        {
            var userId = context.HttpContext.Request.RouteValues["id"];
            var body = context.Arguments.OfType<ChangePasswordBody>().FirstOrDefault();
            var passwordHash = StepUpService.ComputeParamsHash("users.password", body?.NewPassword ?? string.Empty);
            return $"userId={userId};passwordHash={passwordHash};reason={body?.Reason?.Trim()}";
        });

        // POST /api/users/{id}/poster
        group.MapPost("/{id:long}/poster", async (
            long id,
            HttpRequest request,
            FilesServerApi.FilesServerApiClient filesClient,
            UsersServerApi.UsersServerApiClient usersClient) =>
        {
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
        .WithName("UploadUserPoster")
        .RequirePermission(AdminPermissions.UsersRead);

        // DELETE /api/users/{id}/sessions/{deviceId}
        group.MapDelete("/{id:long}/sessions/{deviceId}", async (
            long id,
            string deviceId,
            IdentityServerApi.IdentityServerApiClient identityClient) =>
        {
            await identityClient.RemoveActiveSessionServerAsync(new RemoveActiveSessionServerRequest
            {
                UserId = id,
                DeviceId = deviceId
            });

            return Results.Ok(new { success = true });
        })
        .WithName("RemoveUserSession")
        .RequirePermission(AdminPermissions.UsersSessionsRevoke);

        // DELETE /api/users/{id}/sessions — завершить все активные сессии
        group.MapDelete("/{id:long}/sessions", async (
            long id,
            UserSessionRevocationService revocationService,
            CancellationToken cancellationToken) =>
        {
            var result = await revocationService.RevokeAllAsync(id, cancellationToken);
            return Results.Ok(new
            {
                requestedCount = result.RequestedCount,
                revokedCount = result.RevokedCount,
                failedDeviceIds = result.FailedDeviceIds
            });
        })
        .WithName("RemoveAllUserSessions")
        .RequirePermission(AdminPermissions.UsersSessionsRevoke)
        .RequireStepUp(StepUpActions.UsersSessionsRevokeAll, context =>
            $"userId={context.Request.RouteValues["id"]}");
    }

    private static async Task<GrpcCallResult<T>> SafeCall<T>(Func<Grpc.Core.AsyncUnaryCall<T>> call) where T : class
    {
        try
        {
            return new GrpcCallResult<T>(await call(), true);
        }
        catch
        {
            return new GrpcCallResult<T>(null, false);
        }
    }

    private sealed record GrpcCallResult<T>(T? Value, bool Available) where T : class;

    private sealed record AssignBadgeBody(int BadgeId, int? Priority);
    private sealed record UpdateStorageLimitBody(int StorageLimitGb);
    private sealed record DisableOtpBody(int OtpType, string? Reason);
    private sealed record UpdateProfileBody(string? FirstName, string? LastName, string? Bio, string? Username);
    private sealed record ChangePasswordBody(string? NewPassword, string? Reason);
    private sealed record AccountRestrictionBody(string? Reason);

    private static async Task<IResult> SetAccountRestriction(
        IdentityServerApi.IdentityServerApiClient identityClient,
        long userId,
        AccountRestrictionAction action,
        AccountRestrictionBody? body)
    {
        if (body is null)
            return Results.BadRequest("Invalid request body");

        var response = await identityClient.SetAccountRestrictionServerAsync(new SetAccountRestrictionServerRequest
        {
            UserId = userId,
            Action = action,
            Reason = body.Reason?.Trim() ?? string.Empty
        });

        var restriction = response.Restriction;
        return Results.Ok(new
        {
            accountStatus = restriction.Status.ToString(),
            restrictionReason = restriction.Reason,
            restrictedAt = restriction.RestrictedAt?.ToDateTime(),
            deletionScheduledAt = restriction.DeletionScheduledAt?.ToDateTime()
        });
    }

    private static string RestrictionStepUpParameters(EndpointFilterInvocationContext context, string action)
    {
        var userId = context.HttpContext.Request.RouteValues["id"];
        var body = context.Arguments.OfType<AccountRestrictionBody>().FirstOrDefault();
        return $"userId={userId};action={action};reason={body?.Reason?.Trim()}";
    }
}
