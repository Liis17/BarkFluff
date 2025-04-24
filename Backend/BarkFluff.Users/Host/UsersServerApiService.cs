using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Features.AddDraftUser;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Features.CheckExistUsername;
using BarkFluff.Users.Features.ConfirmUser;
using BarkFluff.Users.Features.FindByLogin;
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
        var command = new AddDraftUserCommand(){ Username = request.Username, Email = request.Email, FirstName = request.FirstName, LastName = request.LastName};
       
        return _mediator.Send(command);
    }

    public override async Task<ConfirmUserResponse> ConfirmUser(ConfirmUserRequest request, ServerCallContext context)
    {
        var command = new ConfirmUserCommand() { UserId = request.UserId };
        
        await _mediator.Send(command);
        
        return new ConfirmUserResponse();
    }
}