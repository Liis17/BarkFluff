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
using BarkFluff.Users.Features.Devices.GetDevicesWithFirebaseTokens;
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
using BarkFluff.Users.Features.SetProfilePictureServer;
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
        var command = new CheckExistEmailQuery() { Email = request.Email?.Trim() };

        return _mediator.Send(command);
    }

    public override Task<CheckExistResponse> CheckExistUsername(CheckExistUsernameRequest request, ServerCallContext context)
    {
        var command = new CheckExistUsernameQuery() { Username = request.Username?.Trim() };

        return _mediator.Send(command);
    }

    public override Task<FindByLoginResponse> FindByLogin(FindByLoginRequest request, ServerCallContext context)
    {
        var command = new FindByLoginQuery() { Username = request.Username?.Trim(), Email = request.Email?.Trim() };

        return _mediator.Send(command);
    }

    public override Task<AddDraftUserResponse> AddDraftUser(AddDraftUserRequest request, ServerCallContext context)
    {
        var command = new AddDraftUserCommand() { Username = request.Username?.Trim(), Email = request.Email?.Trim(), FirstName = request.FirstName?.Trim(), LastName = request.LastName?.Trim() };

        return _mediator.Send(command);
    }

    public override async Task<ConfirmUserResponse> ConfirmUser(ConfirmUserRequest request, ServerCallContext context)
    {
        var command = new ConfirmUserCommand() { UserId = request.UserId };

        await _mediator.Send(command);

        return new ConfirmUserResponse();
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
        var command = new GetUserContactsCommand()
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<AddDraftUserResponse> OverrideDraftUser(AddDraftUserRequest request, ServerCallContext context)
    {
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
        var command = new RemoveUserBadgeCommand
        {
            UserId = request.UserId,
            BadgeId = request.BadgeId
        };

        return _mediator.Send(command);
    }

    public override Task<UpdateUserBadgePriorityResponse> UpdateUserBadgePriority(UpdateUserBadgePriorityRequest request, ServerCallContext context)
    {
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
        var query = new GetAllBadgesQuery
        {
            IncludeInactive = request.IncludeInactive
        };

        return _mediator.Send(query);
    }

    public override Task<UpdateBadgeResponse> UpdateBadge(UpdateBadgeRequest request, ServerCallContext context)
    {
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
        var command = new DeleteBadgeCommand
        {
            Id = request.Id
        };

        return _mediator.Send(command);
    }

    public override Task<ExportDataResponse> ExportData(ExportDataRequest request, ServerCallContext context)
    {
        var command = new ExportDataCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    // Методы для работы с устройствами

    public override Task<RegisterDeviceResponse> RegisterDevice(RegisterDeviceRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_registrations");
        var command = new RegisterDeviceCommand
        {
            DeviceId = Guid.Parse(request.DeviceId),
            UserId = request.UserId,
            OriginalName = request.OriginalName,
            AppName = request.AppName,
            OperationSystem = request.OperationSystem,
            Location = request.Location
        };

        return _mediator.Send(command);
    }

    public override Task<GetUserDevicesResponse> GetUserDevices(GetUserDevicesRequest request, ServerCallContext context)
    {
        var query = new GetUserDevicesQuery
        {
            UserId = request.UserId
        };

        return _mediator.Send(query);
    }

    public override Task<DeleteUserDeviceResponse> DeleteUserDevice(DeleteUserDeviceRequest request, ServerCallContext context)
    {
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
        var user = await _usersStorage.GetUserByUsername(request.Username?.Trim());

        if (user is null || user.IsDraft)
        {
            return new GetUserByUsernameResponse { Found = false };
        }

        // Применение настроек приватности к публичной странице профиля.
        // FRIENDS пока трактуется как NONE — в бэкенде нет системы отношений между пользователями.
        var privacy = await _privacyStorage.GetOrCreate(user.Id);

        if (!privacy.ProfileVisibleOnSite)
        {
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
            }
        }
        catch (Exception)
        {
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
        var command = new UpdateStorageLimitCommand
        {
            UserId = request.UserId,
            StorageLimitGb = request.StorageLimitGb
        };

        return _mediator.Send(command);
    }

    public override Task<SetProfilePictureServerResponse> SetProfilePictureServer(SetProfilePictureServerRequest request, ServerCallContext context)
    {
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
        var query = new GetDevicesWithFirebaseTokensQuery
        {
            UserIds = request.UserIds.ToList()
        };

        return _mediator.Send(query);
    }
}