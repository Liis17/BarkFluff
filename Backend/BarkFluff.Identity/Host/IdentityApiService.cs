using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Features.Auth;
using BarkFluff.Identity.Features.ConfirmAccount;
using BarkFluff.Identity.Features.ConfirmOtpVerification;
using BarkFluff.Identity.Features.CreateAccount;
using BarkFluff.Identity.Features.CreateToken;
using BarkFluff.Identity.Features.DisableOtpVerification;
using BarkFluff.Identity.Features.EnableOtpVerification;
using BarkFluff.Identity.Features.GetActiveSessions;
using BarkFluff.Identity.Features.ListOtpVerification;
using BarkFluff.Identity.Features.Logout;
using BarkFluff.Identity.Features.RemoveActiveSession;
using BarkFluff.Identity.Features.ResetPassword;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Identity.Host;

using Features.ConfirmResetPassword;
using Features.SetPassword;

public class IdentityApiService : BarkFluff.Proto.Identity.IdentityApi.IdentityApiBase
{
    private readonly IMediator _mediator;
    private readonly JwtService _jwtService;
    private readonly MetricsCollector _metrics;

    public IdentityApiService(IMediator mediator, JwtService jwtService, MetricsCollector metrics)
    {
        _mediator = mediator;
        _jwtService = jwtService;
        _metrics = metrics;
    }

    public override async Task<AuthResponse> Auth(AuthRequest request, ServerCallContext context)
    {
        _metrics.Increment("auth_login_attempts");

        var command = new AuthCommand
        {
            Username = request.Username?.Trim(),
            Email = request.Email?.Trim(),
            OtpCode = request.OtpCode,
            Password = request.Password,
        };

        return await _mediator.Send(command);
    }

    public override async Task<CreateTokenResponse> CreateToken(CreateTokenRequest request, ServerCallContext context)
    {
        _metrics.Increment("tokens_refresh_attempts");

        var command = new CreateTokenCommand
        {
            RefreshToken = request.RefreshToken,
        };

        return await _mediator.Send(command);
    }

    public override async Task<CreateAccountResponse> CreateAccount(CreateAccountRequest request, ServerCallContext context)
    {
        _metrics.Increment("account_creation_attempts");

        var command = new CreateAccountCommand
        {
            Username = request.Username?.Trim(),
            Email = request.Email?.Trim(),
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
        };

        return await _mediator.Send(command);
    }

    public override Task<ConfirmAccountResponse> ConfirmAccount(ConfirmAccountRequest request, ServerCallContext context)
    {
        var command = new ConfirmAccountCommand
        {
            Code = request.CodeValue,
            CodeId = request.CodeId,
        };

        return _mediator.Send(command);
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<GetActiveSessionsResponse> GetActiveSessions(GetActiveSessionsRequest request, ServerCallContext context)
    {
        var command = new GetActiveSessionsCommand();

        return _mediator.Send(command);
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<RemoveActiveSessionResponse> RemoveActiveSession(RemoveActiveSessionRequest request, ServerCallContext context)
    {
        _metrics.Increment("session_removal_attempts");

        var command = new RemoveActiveSessionCommand()
        {
            DeviceId = request.DeviceId
        };

        return _mediator.Send(command);
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<EnableOtpVerificationResponse> EnableOtpVerification(EnableOtpVerificationRequest request,
        ServerCallContext context)
    {
        var command = new EnableOtpVerificationCommand()
        {
            OptType = request.OtpType,
        };

        return _mediator.Send(command);
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<ConfirmOtpVerificationResponse> ConfirmOtpVerification(ConfirmOtpVerificationRequest request, ServerCallContext context)
    {
        var command = new ConfirmOtpVerificationCommand()
        {
            OtpCode = request.OtpCode
        };

        return _mediator.Send(command);
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<DisableOtpVerificationResponse> DisableOtpVerification(DisableOtpVerificationRequest request, ServerCallContext context)
    {
        var command = new DisableOtpVerificationCommand
        {
            OtpCode = request.OtpCode,
            OptType = request.OtpType
        };

        return _mediator.Send(command);
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<ListOtpVerificationResponse> ListOtpVerification(ListOtpVerificationRequest request, ServerCallContext context)
    {
        var command = new ListOtpVerificationCommand();

        return _mediator.Send(command);
    }

    public override async Task<ResetPasswordResponse> ResetPassword(ResetPasswordRequest request,
        ServerCallContext context)
    {
        _metrics.Increment("password_reset_requests");

        var command = new ResetPasswordCommand()
        {
            Email = request.Email?.Trim(),
            OtpType = (OtpType)(int)request.OtpType,
            Username = request.Username?.Trim()
        };

        return await _mediator.Send(command);
    }

    public override async Task<ConfirmResetPasswordResponse> ConfirmResetPassword(ConfirmResetPasswordRequest request, ServerCallContext context)
    {
        var command = new ConfirmResetPasswordCommand
        {
            OtpCode = request.OtpCode,
            ResetId = Guid.Parse(request.ResetId)
        };

        var result = await _mediator.Send(command);

        return result;
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override async Task<SetPasswordResponse> SetPassword(SetPasswordRequest request, ServerCallContext context)
    {
        var command = new SetPasswordCommand
        {
            NewPassword = request.Password,
            OldPassword = request.OldPassword
        };

        await _mediator.Send(command);

        return new SetPasswordResponse();
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        return _mediator.Send(new LogoutCommand());
    }
}