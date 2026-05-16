using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.Badges.AssignUserBadge;
using BarkFluff.Users.Features.Badges.Commands;
using BarkFluff.Users.Features.Badges.DeleteBadge;
using BarkFluff.Users.Features.Badges.Queries;
using BarkFluff.Users.Features.Badges.RemoveUserBadge;
using BarkFluff.Users.Features.Badges.UpdateBadge;
using BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.Devices.DeleteUserDevice;
using BarkFluff.Users.Features.Devices.GetAllDevicesWithFirebaseTokens;
using BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokens;
using BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokensByDeviceIds;
using BarkFluff.Users.Features.Devices.GetUserDevices;
using BarkFluff.Users.Features.Devices.RegisterDevice;
using BarkFluff.Users.Features.ExportData;
using BarkFluff.Users.Features.FindByLogin;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.GetUserContacts;
using BarkFluff.Users.Features.ListByIds;
using BarkFluff.Users.Features.OverrideDraftUser;
using BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;
using BarkFluff.Users.Features.SearchUsersServer;
using BarkFluff.Users.Features.Personalization.GetProfilePosterServer;
using BarkFluff.Users.Features.Personalization.SetProfilePosterServer;
using BarkFluff.Users.Features.SetProfilePictureServer;
using BarkFluff.Users.Features.UpdateProfileServer;
using BarkFluff.Users.Features.UpdateStorageLimit;
using BarkFluff.Users.Persistence.Services;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Users.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class UsersServerApiService : UsersServerApi.UsersServerApiBase
{
    private readonly IMediator _mediator;
    private readonly UsersStorage _usersStorage;
    private readonly PrivacyStorage _privacyStorage;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesClient;
    private readonly MetricsCollector _metrics;

    public UsersServerApiService(
        IMediator mediator,
        UsersStorage usersStorage,
        PrivacyStorage privacyStorage,
        PersonalizationStorage personalizationStorage,
        FilesServerApi.FilesServerApiClient filesClient,
        MetricsCollector metrics)
    {
        _mediator = mediator;
        _usersStorage = usersStorage;
        _privacyStorage = privacyStorage;
        _personalizationStorage = personalizationStorage;
        _filesClient = filesClient;
        _metrics = metrics;
    }


    public override Task<CheckExistResponse> CheckExistEmail(CheckExistEmailRequest request, ServerCallContext context)
    {
        _metrics.Increment("existence_checks");
        var command = new CheckExistEmailQuery() { Email = request.Email?.Trim() };

        return _mediator.Send(command);
    }

    public override Task<CheckExistResponse> CheckExistUsername(CheckExistUsernameRequest request, ServerCallContext context)
    {
        _metrics.Increment("existence_checks");
        var command = new CheckExistUsernameQuery() { Username = request.Username?.Trim() };

        return _mediator.Send(command);
    }

    public override Task<FindByLoginResponse> FindByLogin(FindByLoginRequest request, ServerCallContext context)
    {
        _metrics.Increment("login_lookups");
        var command = new FindByLoginQuery() { Username = request.Username?.Trim(), Email = request.Email?.Trim() };

        return _mediator.Send(command);
    }

    public override async Task<AddDraftUserResponse> AddDraftUser(AddDraftUserRequest request, ServerCallContext context)
    {
        _metrics.Increment("drafts_create_requests");
        try
        {
            var command = new AddDraftUserCommand() { Username = request.Username?.Trim(), Email = request.Email?.Trim(), FirstName = request.FirstName?.Trim(), LastName = request.LastName?.Trim() };
            var response = await _mediator.Send(command);
            _metrics.Increment("drafts_created");
            _metrics.Set("last_draft_created_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return response;
        }
        catch
        {
            _metrics.Increment("drafts_create_errors");
            throw;
        }
    }

    public override async Task<ConfirmUserResponse> ConfirmUser(ConfirmUserRequest request, ServerCallContext context)
    {
        _metrics.Increment("users_confirm_requests");
        try
        {
            var command = new ConfirmUserCommand() { UserId = request.UserId };
            await _mediator.Send(command);
            _metrics.Increment("users_confirmed");
            _metrics.Set("last_user_confirmed_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return new ConfirmUserResponse();
        }
        catch
        {
            _metrics.Increment("users_confirm_errors");
            throw;
        }
    }

    public override async Task<GetByIdResponse> GetById(GetByIdRequest request, ServerCallContext context)
    {
        _metrics.Increment("user_lookups");
        var query = new GetUserQuery { UserId = request.UserId };
        var res = await _mediator.Send(query);

        return new GetByIdResponse { User = res.User };
    }

    public override Task<GetUserContactsResponse> GetUserContacts(GetUserContactsRequest request, ServerCallContext context)
    {
        _metrics.Increment("contact_lookups");
        var command = new GetUserContactsCommand()
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<AddDraftUserResponse> OverrideDraftUser(AddDraftUserRequest request, ServerCallContext context)
    {
        _metrics.Increment("drafts_overridden");
        var command = new OverrideDraftUserCommand()
        {
            LastName = request.LastName?.Trim(),
            FirstName = request.FirstName?.Trim(),
            Email = request.Email?.Trim(),
            Username = request.Username?.Trim(),
        };

        return _mediator.Send(command);
    }

    public override async Task<ListByIdsResponse> ListByIds(ListByIdsRequest request, ServerCallContext context)
    {
        _metrics.Increment("user_lookups");
        var command = new ListByIdsCommand()
        {
            Ids = request.Ids.ToList()
        };

        return await _mediator.Send(command);
    }

    // Методы для работы с баджами

    public override Task<AssignUserBadgeResponse> AssignUserBadge(AssignUserBadgeRequest request, ServerCallContext context)
    {
        _metrics.Increment("badges_assigned");
        var command = new AssignUserBadgeCommand
        {
            UserId = request.UserId,
            BadgeId = request.BadgeId,
            Priority = request.Priority
        };

        return _mediator.Send(command);
    }

    public override Task<RemoveUserBadgeResponse> RemoveUserBadge(RemoveUserBadgeRequest request, ServerCallContext context)
    {
        _metrics.Increment("badges_removed");
        var command = new RemoveUserBadgeCommand
        {
            UserId = request.UserId,
            BadgeId = request.BadgeId
        };

        return _mediator.Send(command);
    }

    public override Task<UpdateUserBadgePriorityResponse> UpdateUserBadgePriority(UpdateUserBadgePriorityRequest request, ServerCallContext context)
    {
        _metrics.Increment("badges_priority_updated");
        var command = new UpdateUserBadgePriorityCommand
        {
            UserId = request.UserId,
            BadgeId = request.BadgeId,
            NewPriority = request.NewPriority
        };

        return _mediator.Send(command);
    }

    public override Task<CreateBadgeResponse> CreateBadge(CreateBadgeRequest request, ServerCallContext context)
    {
        _metrics.Increment("badges_created");
        var command = new CreateBadgeCommand
        {
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl
        };

        return _mediator.Send(command);
    }

    public override Task<GetAllBadgesResponse> GetAllBadges(GetAllBadgesRequest request, ServerCallContext context)
    {
        _metrics.Increment("badge_lookups");
        var query = new GetAllBadgesQuery
        {
            IncludeInactive = request.IncludeInactive
        };

        return _mediator.Send(query);
    }

    public override Task<UpdateBadgeResponse> UpdateBadge(UpdateBadgeRequest request, ServerCallContext context)
    {
        _metrics.Increment("badges_updated");
        var command = new UpdateBadgeCommand
        {
            Id = request.Id,
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            IsActive = request.IsActive
        };

        return _mediator.Send(command);
    }

    public override Task<DeleteBadgeResponse> DeleteBadge(DeleteBadgeRequest request, ServerCallContext context)
    {
        _metrics.Increment("badges_deleted");
        var command = new DeleteBadgeCommand
        {
            Id = request.Id
        };

        return _mediator.Send(command);
    }

    public override async Task<ExportDataResponse> ExportData(ExportDataRequest request, ServerCallContext context)
    {
        _metrics.Increment("data_exports");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var command = new ExportDataCommand
            {
                UserId = request.UserId
            };

            var response = await _mediator.Send(command);
            _metrics.Add("data_export_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics.Set("last_data_export_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return response;
        }
        catch
        {
            _metrics.Increment("data_export_errors");
            throw;
        }
    }

    // Методы для работы с устройствами

    public override async Task<RegisterDeviceResponse> RegisterDevice(RegisterDeviceRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_registrations");
        var response = await _mediator.Send(new RegisterDeviceCommand
        {
            DeviceId = Guid.Parse(request.DeviceId),
            UserId = request.UserId,
            OriginalName = request.OriginalName,
            AppName = request.AppName,
            OperationSystem = request.OperationSystem,
            Location = request.Location
        });
        _metrics.Set("last_device_registered_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return response;
    }

    public override Task<GetUserDevicesResponse> GetUserDevices(GetUserDevicesRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_lookups");
        var query = new GetUserDevicesQuery
        {
            UserId = request.UserId
        };

        return _mediator.Send(query);
    }

    public override Task<DeleteUserDeviceResponse> DeleteUserDevice(DeleteUserDeviceRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_deletions");
        var command = new DeleteUserDeviceCommand
        {
            DeviceId = Guid.Parse(request.DeviceId),
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    // Получение публичной информации пользователя по юзернейму (для веб-сервера)

    public override async Task<GetUserByUsernameResponse> GetUserByUsername(
        GetUserByUsernameRequest request, ServerCallContext context)
    {
        _metrics.Increment("public_profile_views");
        var user = await _usersStorage.GetUserByUsername(request.Username?.Trim());

        if (user is null || user.IsDraft)
        {
            _metrics.Increment("public_profile_not_found");
            return new GetUserByUsernameResponse { Found = false };
        }

        // Применение настроек приватности к публичной странице профиля.
        // FRIENDS пока трактуется как NONE — в бэкенде нет системы отношений между пользователями.
        var privacy = await _privacyStorage.GetOrCreate(user.Id);

        if (!privacy.ProfileVisibleOnSite)
        {
            _metrics.Increment("public_profile_hidden");
            return new GetUserByUsernameResponse { Found = false };
        }

        var bio = privacy.BioVisibility == Domain.ProfileFieldVisibility.All
            ? (user.Bio ?? string.Empty)
            : string.Empty;

        var avatar = privacy.AvatarVisibility == Domain.ProfileFieldVisibility.All
            ? (user.ProfilePicture ?? string.Empty)
            : string.Empty;

        // Получаем постер профиля через персонализацию
        var posterUrl = string.Empty;
        try
        {
            var personalization = await _personalizationStorage.Get(user.Id);
            if (!string.IsNullOrEmpty(personalization?.ProfilePosterFileId))
            {
                var fileDataResponse = await _filesClient.GetFileDataAsync(
                    new GetFileDataRequest { FileId = personalization.ProfilePosterFileId });
                posterUrl = fileDataResponse.FileInfo.FileUrl ?? string.Empty;
                _metrics.Increment("files_fetch_success");
            }
        }
        catch (Exception)
        {
            _metrics.Increment("files_fetch_errors");
            // Не блокируем ответ, если постер недоступен
            posterUrl = string.Empty;
        }

        return new GetUserByUsernameResponse
        {
            Found = true,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Username = user.Username,
            Bio = bio,
            ProfilePicture = avatar,
            ProfilePosterUrl = posterUrl,
        };
    }

    public override Task<GetUserPrivacyResponse> GetUserPrivacy(GetUserPrivacyRequest request, ServerCallContext context)
    {
        return _mediator.Send(new GetUserPrivacyServerQuery { UserId = request.UserId });
    }

    // Поиск пользователей (для админ-панели)

    public override Task<SearchUsersServerResponse> SearchUsersServer(SearchUsersServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("user_searches");
        var query = new SearchUsersServerQuery
        {
            Query = request.Query,
            Offset = request.Offset,
            Size = request.Size
        };

        return _mediator.Send(query);
    }

    public override Task<UpdateStorageLimitResponse> UpdateStorageLimit(UpdateStorageLimitRequest request, ServerCallContext context)
    {
        _metrics.Increment("storage_limit_updates");
        var command = new UpdateStorageLimitCommand
        {
            UserId = request.UserId,
            StorageLimitGb = request.StorageLimitGb
        };

        return _mediator.Send(command);
    }

    public override Task<SetProfilePictureServerResponse> SetProfilePictureServer(SetProfilePictureServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_avatar_updates");
        var command = new SetProfilePictureServerCommand
        {
            UserId = request.UserId,
            ProfilePictureUrl = request.ProfilePictureUrl,
            ProfilePicturePreviewUrl = request.ProfilePicturePreviewUrl
        };

        return _mediator.Send(command);
    }

    public override Task<GetDevicesWithFirebaseTokensResponse> GetDevicesWithFirebaseTokens(GetDevicesWithFirebaseTokensRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_lookups");
        var query = new GetDevicesWithFirebaseTokensQuery
        {
            UserIds = request.UserIds.ToList()
        };

        return _mediator.Send(query);
    }

    public override Task<GetDevicesWithFirebaseTokensResponse> GetDevicesWithFirebaseTokensByDeviceIds(GetDevicesWithFirebaseTokensByDeviceIdsRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_lookups_by_device_id");

        var deviceIds = new List<Guid>(request.DeviceIds.Count);
        foreach (var raw in request.DeviceIds)
        {
            if (Guid.TryParse(raw, out var id))
                deviceIds.Add(id);
        }

        var query = new GetDevicesWithFirebaseTokensByDeviceIdsQuery
        {
            DeviceIds = deviceIds
        };

        return _mediator.Send(query);
    }

    public override Task<GetDevicesWithFirebaseTokensResponse> GetAllDevicesWithFirebaseTokens(GetAllDevicesWithFirebaseTokensRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_lookups_all");
        return _mediator.Send(new GetAllDevicesWithFirebaseTokensQuery());
    }

    public override Task<UpdateProfileServerResponse> UpdateProfileServer(UpdateProfileServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_updates_server");
        return _mediator.Send(new UpdateProfileServerCommand
        {
            UserId = request.UserId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Bio = request.Bio,
            Username = request.Username
        });
    }

    public override Task<SetProfilePosterServerResponse> SetProfilePosterServer(SetProfilePosterServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_poster_updates");
        return _mediator.Send(new SetProfilePosterServerCommand
        {
            UserId = request.UserId,
            PosterFileId = string.IsNullOrEmpty(request.PosterFileId) ? null : request.PosterFileId
        });
    }

    public override Task<GetProfilePosterServerResponse> GetProfilePosterServer(GetProfilePosterServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_poster_lookups");
        return _mediator.Send(new GetProfilePosterServerQuery { UserId = request.UserId });
    }
}