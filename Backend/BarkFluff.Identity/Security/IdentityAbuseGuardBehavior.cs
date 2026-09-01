using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Features.Auth;
using BarkFluff.Identity.Features.CreateAccount;
using BarkFluff.Identity.Features.EnableOtpVerification;
using BarkFluff.Identity.Features.ResetPassword;

using MediatR;

namespace BarkFluff.Identity.Security;

public sealed class IdentityAbuseGuardBehavior<TRequest, TResponse>(
    IIdentityAbuseGuard guard,
    RequestContext requestContext,
    UserContext userContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!TryGetOperation(request, userContext, out var operation, out var subject, out var countSubject))
            return await next(cancellationToken);

        await guard.EnsureRequestAllowedAsync(
            operation,
            requestContext.TrustedIpAddress,
            subject,
            countSubject,
            cancellationToken);

        return await next(cancellationToken);
    }

    private static bool TryGetOperation(
        TRequest request,
        UserContext userContext,
        out IdentityAbuseOperation operation,
        out string? subject,
        out bool countSubject)
    {
        switch (request)
        {
            case AuthCommand:
                operation = IdentityAbuseOperation.Auth;
                subject = null;
                countSubject = false;
                return true;
            case CreateAccountCommand createAccount:
                operation = IdentityAbuseOperation.CreateAccount;
                subject = FirstSubject(createAccount.Email, createAccount.Username);
                countSubject = true;
                return true;
            case Features.ConfirmAccount.ConfirmAccountCommand:
                operation = IdentityAbuseOperation.ConfirmAccount;
                subject = null;
                countSubject = false;
                return true;
            case ResetPasswordCommand resetPassword:
                operation = IdentityAbuseOperation.ResetPassword;
                subject = FirstSubject(resetPassword.Username, resetPassword.Email);
                countSubject = true;
                return true;
            case Features.ConfirmResetPassword.ConfirmResetPasswordCommand:
                operation = IdentityAbuseOperation.ConfirmResetPassword;
                subject = null;
                countSubject = false;
                return true;
            case EnableOtpVerificationCommand:
                operation = IdentityAbuseOperation.EnableOtpVerification;
                subject = userContext.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                countSubject = true;
                return true;
            case Features.ConfirmOtpVerification.ConfirmOtpVerificationCommand:
                operation = IdentityAbuseOperation.ConfirmOtpVerification;
                subject = null;
                countSubject = false;
                return true;
            case Features.DisableOtpVerification.DisableOtpVerificationCommand:
                operation = IdentityAbuseOperation.DisableOtpVerification;
                subject = null;
                countSubject = false;
                return true;
            default:
                operation = default;
                subject = null;
                countSubject = false;
                return false;
        }
    }

    private static string? FirstSubject(string? primary, string? secondary) =>
        string.IsNullOrWhiteSpace(primary) ? secondary : primary;
}
