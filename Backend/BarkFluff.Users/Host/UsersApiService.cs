using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.SetPassword;
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
}