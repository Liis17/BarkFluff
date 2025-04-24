using BarkFluff.Proto.Identity;
using BarkFluff.Identity.Features.Auth;
using BarkFluff.Identity.Features.CreateAccount;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Services;
using BarkFluff.Shared.Exceptions.Identity;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Identity.Host;

public class IdentityApiService : BarkFluff.Proto.Identity.IdentityApi.IdentityApiBase
{
    private readonly IMediator _mediator;
    private readonly JwtService _jwtService;

    public IdentityApiService(IMediator mediator, JwtService jwtService)
    {
        _mediator = mediator;
        _jwtService = jwtService;
    }

    public override async Task<AuthResponse> Auth(AuthRequest request, ServerCallContext context)
    {
        var command = new AuthCommand
        {
            Username = request.Username,
            Email = request.Email,
            Password = request.Password
        };

        return await _mediator.Send(command);
    }

    public override async Task<CreateTokenResponse> CreateToken(CreateTokenRequest request, ServerCallContext context)
    {
        var command = new CreateTokenCommand
        {
            RefreshToken = request.RefreshToken,
        };

        return await _mediator.Send(command);
    }

    public override async Task<CreateAccountResponse> CreateAccount(CreateAccountRequest request, ServerCallContext context)
    {
        var command = new CreateAccountCommand
        {
            Username = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        return await _mediator.Send(command);
    }

    public override async Task<GenerateTestTokenResponse> GenerateTestToken(GenerateTestTokenRequest request, ServerCallContext context)
    {
        var token = _jwtService.GenerateUserToken(request.UserId);

        return new GenerateTestTokenResponse() { Token = token.Value };
    }
}