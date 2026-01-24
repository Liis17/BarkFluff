using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.Badges.AssignUserBadge;
using BarkFluff.Users.Features.Badges.Commands;
using BarkFluff.Users.Features.Badges.Queries;
using BarkFluff.Users.Features.Badges.RemoveUserBadge;
using BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.ExportData;
using BarkFluff.Users.Features.FindByLogin;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.GetUserContacts;
using BarkFluff.Users.Features.ListByIds;
using BarkFluff.Users.Features.OverrideDraftUser;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Users.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class UsersServerApiService : UsersServerApi.UsersServerApiBase
{
    private readonly IMediator _mediator;

    public UsersServerApiService(IMediator mediator)
    {
        _mediator = mediator;
    }


    public override Task<CheckExistResponse> CheckExistEmail(CheckExistEmailRequest request, ServerCallContext context)
    {
        var command = new CheckExistEmailQuery() { Email = request.Email };

        return _mediator.Send(command);
    }

    public override Task<CheckExistResponse> CheckExistUsername(CheckExistUsernameRequest request, ServerCallContext context)
    {
        var command = new CheckExistUsernameQuery() { Username = request.Username };

        return _mediator.Send(command);
    }

    public override Task<FindByLoginResponse> FindByLogin(FindByLoginRequest request, ServerCallContext context)
    {
        var command = new FindByLoginQuery() { Username = request.Username, Email = request.Email };

        return _mediator.Send(command);
    }

    public override Task<AddDraftUserResponse> AddDraftUser(AddDraftUserRequest request, ServerCallContext context)
    {
        var command = new AddDraftUserCommand() { Username = request.Username, Email = request.Email, FirstName = request.FirstName, LastName = request.LastName };

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
            LastName = request.LastName,
            FirstName = request.FirstName,
            Email = request.Email,
            Username = request.Username,
        };

        return _mediator.Send(command);
    }

    public override async Task<ListByIdsResponse> ListByIds(ListByIdsRequest request, ServerCallContext context)
    {
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

    public override Task<ExportDataResponse> ExportData(ExportDataRequest request, ServerCallContext context)
    {
        var command = new ExportDataCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }
}