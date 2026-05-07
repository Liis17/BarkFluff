using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Features.Badges.GetUserBadges;
using BarkFluff.Users.Features.ChangeBio;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Features.ChangeUsername;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using BarkFluff.Users.Features.Devices.GetCurrentDevice;
using BarkFluff.Users.Features.Devices.GetDevices;
using BarkFluff.Users.Features.Devices.RenameDevice;
using BarkFluff.Users.Features.Devices.SetFirebaseToken;
using BarkFluff.Users.Features.Devices.SetNotificationsEnabled;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.Personalization.GetPersonalization;
using BarkFluff.Users.Features.Personalization.GetProfilePoster;
using BarkFluff.Users.Features.Personalization.SetProfilePoster;
using BarkFluff.Users.Features.Personalization.UpdatePersonalization;
using BarkFluff.Users.Features.Privacy.GetPrivacySettings;
using BarkFluff.Users.Features.Privacy.UpdatePrivacySettings;
using BarkFluff.Users.Features.SetProfilePicture;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Users.Host;

using Features.SearchUsers;

using Proto.Shared;

[Authorize(Policy = nameof(TokenType.User))]
public class UsersApiService : BarkFluff.Proto.Users.UsersApi.UsersApiBase
{
    private readonly IMediator _mediator;
    private readonly MetricsCollector _metrics;

    public UsersApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public override async Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        _metrics.Increment("user_lookups");
        var query = new GetUserQuery { UserId = request.UserId == 0 ? null : request.UserId };
        return await _mediator.Send(query);
    }

    public override async Task<SetProfilePictureResponse> SetProfilePicture(SetProfilePictureRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.FileId))
            _metrics.Increment("profile_avatar_removals");
        else
            _metrics.Increment("profile_avatar_updates");

        var command = new SetProfilePictureCommand
        {
            FileId = string.IsNullOrEmpty(request.FileId) ? null : Guid.Parse(request.FileId)
        };

        return await _mediator.Send(command);
    }

    [AllowAnonymous]
    public override Task<CheckExistResponse> CheckExistEmail(CheckExistEmailRequest request, ServerCallContext context)
    {
        _metrics.Increment("existence_checks");
        var command = new CheckExistEmailQuery() { Email = request.Email?.Trim() };

        return _mediator.Send(command);
    }

    [AllowAnonymous]
    public override Task<CheckExistResponse> CheckExistUsername(CheckExistUsernameRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("existence_checks");
        var command = new CheckExistUsernameQuery() { Username = request.Username?.Trim() };

        return _mediator.Send(command);
    }

    public override async Task<ChangeNameResponse> ChangeName(ChangeNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_name_updates");
        var command = new ChangeNameCommand()
        {
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
        };

        await _mediator.Send(command);

        return new ChangeNameResponse();
    }

    public override async Task<ChangeUsernameResponse> ChangeUsername(ChangeUsernameRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_username_updates");
        var command = new ChangeUsernameCommand()
        {
            Username = request.Username?.Trim()
        };

        await _mediator.Send(command);

        return new ChangeUsernameResponse();
    }

    public override async Task<ChangeBioResponse> ChangeBio(ChangeBioRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_bio_updates");
        var command = new ChangeBioCommand() { Bio = request.Bio };

        await _mediator.Send(command);

        return new ChangeBioResponse();
    }

    public override async Task<SearchUsersResponse> SearchUsers(SearchUsersRequest request, ServerCallContext context)
    {
        _metrics.Increment("user_searches");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            request.Pagination ??= new PageRequest()
            {
                Size = 10,
                Offset = 0
            };

            var command = new SearchUsersQuery { Query = request.Query, Size = request.Pagination.Size, Skip = request.Pagination.Offset };

            var response = await _mediator.Send(command);
            _metrics.Add("user_search_duration_ms_total", sw.ElapsedMilliseconds);
            _metrics.Set("last_user_search_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return response;
        }
        catch
        {
            _metrics.Increment("user_search_errors");
            throw;
        }
    }

    public override Task<GetUserBadgesResponse> GetUserBadges(GetUserBadgesRequest request, ServerCallContext context)
    {
        _metrics.Increment("badge_lookups");
        var query = new GetUserBadgesQuery
        {
            UserId = request.UserId,
            Limit = request.Limit
        };

        return _mediator.Send(query);
    }

    // Методы для работы с устройствами

    public override Task<GetDevicesResponse> GetDevices(GetDevicesRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_lookups");
        var query = new GetDevicesQuery();
        return _mediator.Send(query);
    }

    public override Task<GetCurrentDeviceResponse> GetCurrentDevice(GetCurrentDeviceRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_lookups");
        var query = new GetCurrentDeviceQuery();
        return _mediator.Send(query);
    }

    public override Task<RenameDeviceResponse> RenameDevice(RenameDeviceRequest request, ServerCallContext context)
    {
        _metrics.Increment("device_renames");
        var command = new RenameDeviceCommand
        {
            DeviceId = Guid.Parse(request.DeviceId),
            CustomName = request.CustomName
        };

        return _mediator.Send(command);
    }

    public override async Task<SetFirebaseTokenResponse> SetFirebaseToken(SetFirebaseTokenRequest request, ServerCallContext context)
    {
        _metrics.Increment("firebase_token_updates");
        var command = new SetFirebaseTokenCommand
        {
            FirebaseToken = request.FirebaseToken
        };

        await _mediator.Send(command);

        return new SetFirebaseTokenResponse();
    }

    public override async Task<SetNotificationsEnabledResponse> SetNotificationsEnabled(SetNotificationsEnabledRequest request, ServerCallContext context)
    {
        _metrics.Increment("notifications_toggles");
        var command = new SetNotificationsEnabledCommand
        {
            Enabled = request.Enabled
        };

        await _mediator.Send(command);

        return new SetNotificationsEnabledResponse();
    }

    public override Task<GetPrivacySettingsResponse> GetPrivacySettings(GetPrivacySettingsRequest request, ServerCallContext context)
    {
        return _mediator.Send(new GetPrivacySettingsQuery());
    }

    public override async Task<UpdatePrivacySettingsResponse> UpdatePrivacySettings(UpdatePrivacySettingsRequest request, ServerCallContext context)
    {
        _metrics.Increment("privacy_updates");
        await _mediator.Send(new UpdatePrivacySettingsCommand { Settings = request.Settings });
        return new UpdatePrivacySettingsResponse();
    }

    public override Task<GetPersonalizationResponse> GetPersonalization(GetPersonalizationRequest request, ServerCallContext context)
    {
        return _mediator.Send(new GetPersonalizationQuery());
    }

    public override async Task<UpdatePersonalizationResponse> UpdatePersonalization(UpdatePersonalizationRequest request, ServerCallContext context)
    {
        _metrics.Increment("personalization_updates");
        await _mediator.Send(new UpdatePersonalizationCommand { Personalization = request.Personalization });
        return new UpdatePersonalizationResponse();
    }

    public override Task<GetProfilePosterResponse> GetProfilePoster(GetProfilePosterRequest request, ServerCallContext context)
    {
        return _mediator.Send(new GetProfilePosterQuery());
    }

    public override async Task<SetProfilePosterResponse> SetProfilePoster(SetProfilePosterRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_poster_updates");
        var fileId = string.IsNullOrEmpty(request.ProfilePosterFileId) ? null : request.ProfilePosterFileId;
        await _mediator.Send(new SetProfilePosterCommand { ProfilePosterFileId = fileId });
        return new SetProfilePosterResponse();
    }
}