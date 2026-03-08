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
using BarkFluff.Users.Features.GetUser;
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

    public override Task<SetProfilePictureResponse> SetProfilePicture(SetProfilePictureRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_updates");
        var command = new SetProfilePictureCommand
        {
            FileId = string.IsNullOrEmpty(request.FileId) ? null : Guid.Parse(request.FileId)
        };

        return _mediator.Send(command);
    }

    [AllowAnonymous]
    public override Task<CheckExistResponse> CheckExistEmail(CheckExistEmailRequest request, ServerCallContext context)
    {
        var command = new CheckExistEmailQuery() { Email = request.Email?.Trim() };

        return _mediator.Send(command);
    }

    [AllowAnonymous]
    public override Task<CheckExistResponse> CheckExistUsername(CheckExistUsernameRequest request,
        ServerCallContext context)
    {
        var command = new CheckExistUsernameQuery() { Username = request.Username?.Trim() };

        return _mediator.Send(command);
    }

    public override async Task<ChangeNameResponse> ChangeName(ChangeNameRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_updates");
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
        _metrics.Increment("profile_updates");
        var command = new ChangeUsernameCommand()
        {
            Username = request.Username?.Trim()
        };

        await _mediator.Send(command);

        return new ChangeUsernameResponse();
    }

    public override async Task<ChangeBioResponse> ChangeBio(ChangeBioRequest request, ServerCallContext context)
    {
        _metrics.Increment("profile_updates");
        var command = new ChangeBioCommand() { Bio = request.Bio };

        await _mediator.Send(command);

        return new ChangeBioResponse();
    }

    public override Task<SearchUsersResponse> SearchUsers(SearchUsersRequest request, ServerCallContext context)
    {
        _metrics.Increment("user_searches");
        request.Pagination ??= new PageRequest()
        {
            Size = 10,
            Offset = 0
        };

        var command = new SearchUsersQuery { Query = request.Query, Size = request.Pagination.Size, Skip = request.Pagination.Offset };

        return _mediator.Send(command);
    }

    public override Task<GetUserBadgesResponse> GetUserBadges(GetUserBadgesRequest request, ServerCallContext context)
    {
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
        var query = new GetDevicesQuery();
        return _mediator.Send(query);
    }

    public override Task<GetCurrentDeviceResponse> GetCurrentDevice(GetCurrentDeviceRequest request, ServerCallContext context)
    {
        var query = new GetCurrentDeviceQuery();
        return _mediator.Send(query);
    }

    public override Task<RenameDeviceResponse> RenameDevice(RenameDeviceRequest request, ServerCallContext context)
    {
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
}