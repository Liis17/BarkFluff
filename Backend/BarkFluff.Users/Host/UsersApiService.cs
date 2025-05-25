using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Features.ChangeUsername;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.SetPassword;
using BarkFluff.Users.Features.SetProfilePicture;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Users.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class UsersApiService : BarkFluff.Proto.Users.UsersApi.UsersApiBase
{
    private readonly IMediator _mediator;

    public UsersApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<SetPasswordResponse> SetPassword(SetPasswordRequest request, ServerCallContext context)
    {
        var command = new SetPasswordCommand { Password = request.Password };
        await _mediator.Send(command);
        
        return new SetPasswordResponse();
    }

    public override async Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        var query = new GetUserQuery { UserId = request.UserId == 0 ? null : request.UserId };
        return await _mediator.Send(query);
    }

    public override Task<SetProfilePictureResponse> SetProfilePicture(SetProfilePictureRequest request, ServerCallContext context)
    {
        var command = new SetProfilePictureCommand
        {
            FileId = Guid.Parse(request.FileId)
        };
        
        return _mediator.Send(command);
    }

    [AllowAnonymous]
    public override Task<CheckExistResponse> CheckExistEmail(CheckExistEmailRequest request, ServerCallContext context)
    {
        var command = new CheckExistEmailQuery() { Email = request.Email };
        
        return _mediator.Send(command);
    }

    [AllowAnonymous]
    public override Task<CheckExistResponse> CheckExistUsername(CheckExistUsernameRequest request,
        ServerCallContext context)
    {
        var command = new CheckExistUsernameQuery() { Username = request.Username };
        
        return _mediator.Send(command);
    }

    public override async Task<ChangeNameResponse> ChangeName(ChangeNameRequest request, ServerCallContext context)
    {
        var command = new ChangeNameCommand()
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
        };
        
        await _mediator.Send(command);

        return new ChangeNameResponse();
    }

    public override async Task<ChangeUsernameResponse> ChangeUsername(ChangeUsernameRequest request, ServerCallContext context)
    {
        var command = new ChangeUsernameCommand()
        {
            Username = request.Username
        };
        
        await _mediator.Send(command);
        
        return new ChangeUsernameResponse();
    }
}