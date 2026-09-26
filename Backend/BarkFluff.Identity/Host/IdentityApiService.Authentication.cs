using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Identity.Host;

public partial class IdentityApiService
{
    public override Task<GetAuthCapabilitiesResponse> GetAuthCapabilities(GetAuthCapabilitiesRequest request, ServerCallContext context) => _authentication!.Capabilities(context.CancellationToken);
    public override Task<AuthChallengeResponse> BeginRegistration(BeginRegistrationRequest request, ServerCallContext context) => _authentication!.BeginRegistration(request, context.CancellationToken);
    public override Task<AuthChallengeResponse> BeginSignIn(BeginSignInRequest request, ServerCallContext context) => _authentication!.BeginSignIn(request, context.CancellationToken);
    public override Task<AuthChallengeResponse> GetAuthChallenge(AuthChallengeReference request, ServerCallContext context) => _authentication!.Status(request, context.CancellationToken);
    public override Task<CompleteAuthChallengeResponse> CompleteAuthChallenge(CompleteAuthChallengeRequest request, ServerCallContext context) => _authentication!.Complete(request, context.CancellationToken);
    public override Task<AuthChallengeResponse> CancelAuthChallenge(AuthChallengeReference request, ServerCallContext context) => _authentication!.Cancel(request, context.CancellationToken);
    public override Task<AuthChallengeResponse> ResendAuthChallenge(AuthChallengeReference request, ServerCallContext context) => _authentication!.Resend(request, context.CancellationToken);
    public override Task<AuthChallengeResponse> BeginPasswordRecovery(BeginPasswordRecoveryRequest request, ServerCallContext context) => _authentication!.BeginPasswordRecovery(request, context.CancellationToken);
    public override Task<SetPasswordResponse> SetRecoveredPassword(SetRecoveredPasswordRequest request, ServerCallContext context) => _authentication!.SetRecoveredPassword(request, context.CancellationToken);

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<SecuritySettingsResponse> GetSecuritySettings(GetSecuritySettingsRequest request, ServerCallContext context) => _authentication!.GetSecuritySettings(context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<LoginNotificationSettingsResponse> GetLoginNotificationSettings(GetLoginNotificationSettingsRequest request, ServerCallContext context) => _authentication!.GetLoginNotificationSettings(context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<LoginNotificationSettingsResponse> SetLoginNotificationChannel(SetLoginNotificationChannelRequest request, ServerCallContext context) => _authentication!.SetLoginNotificationChannel(request, context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<AuthChallengeResponse> BeginReauthentication(BeginReauthenticationRequest request, ServerCallContext context) => _authentication!.BeginReauthentication(request, context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<SecuritySettingsResponse> UpdateSecuritySettings(UpdateSecuritySettingsRequest request, ServerCallContext context) => _authentication!.UpdateSecuritySettings(request, false, context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<SecuritySettingsResponse> UnlinkTelegram(UpdateSecuritySettingsRequest request, ServerCallContext context) => _authentication!.UpdateSecuritySettings(request, true, context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<AuthChallengeResponse> BeginTelegramBinding(SecurityProofRequest request, ServerCallContext context) => _authentication!.BeginTelegramBinding(request, context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<AuthChallengeResponse> BeginEmailBinding(BeginEmailBindingRequest request, ServerCallContext context) => _authentication!.BeginEmailBinding(request, context.CancellationToken);
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<RecoveryCodesResponse> GenerateRecoveryCodes(SecurityProofRequest request, ServerCallContext context) => _authentication!.GenerateRecoveryCodes(request, context.CancellationToken);
}
