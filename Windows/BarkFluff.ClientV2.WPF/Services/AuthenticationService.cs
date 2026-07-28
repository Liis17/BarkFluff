using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.ClientV2.WPF.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;

    public AuthenticationService(WebApiClient webApi, IClientSession session)
    {
        _webApi = webApi;
        _session = session;
    }

    public async Task<LoginResult> LoginAsync(
        string loginOrEmail,
        string password,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var connection = _session.CurrentConnection;
        if (connection is null)
        {
            return LoginResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var isEmail = loginOrEmail.Contains('@');
        var result = await _webApi.Authorizations(
            isEmail ? loginOrEmail : string.Empty,
            isEmail ? string.Empty : loginOrEmail,
            password,
            otpCode,
            connection.ConnectionParameters);

        if (!result.Error.IsSuccess || result.accessToken is null || result.refreshToken is null)
        {
            var requiresTwoFactor = result.getMeOtpCode;
            return LoginResult.Failure(
                requiresTwoFactor && !string.IsNullOrWhiteSpace(otpCode)
                    ? "Error_LoginTwoFactorInvalid"
                    : requiresTwoFactor ? "Error_LoginTwoFactorRequired" : "Error_LoginFailed",
                requiresTwoFactor);
        }

        connection.ConnectionParameters.AccessToken = result.accessToken;
        connection.ConnectionParameters.RefreshToken = result.refreshToken;
        var reinitialized = _webApi.CreateAC(
            connection.ConnectionParameters,
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            "BarkFluff",
            "2.0",
            string.Empty);
        return reinitialized.IsSuccess
            ? LoginResult.Success()
            : LoginResult.Failure("Error_LoginUnavailable");
    }

    public async Task<FastAuthQrCode?> CreateFastAuthQrCodeAsync(CancellationToken cancellationToken = default)
    {
        var connection = _session.CurrentConnection;
        if (connection is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var initialized = _webApi.CreateFastAuthClient(
            connection.ConnectionParameters,
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            "BarkFluff",
            "2.0",
            string.Empty);
        if (!initialized.IsSuccess)
        {
            return null;
        }

        var result = await _webApi.GenerateFastAuthToken(TokenFormat.Qr);
        if (!result.Item1.IsSuccess || result.Item2?.Token?.Value is not { Length: > 0 } base64Png)
        {
            return null;
        }

        return new FastAuthQrCode(base64Png, result.Item2.ExpiresAt.ToDateTimeOffset());
    }
}
