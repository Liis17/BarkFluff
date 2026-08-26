using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;

using System.Runtime.CompilerServices;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task LoginAsync_TwoFactorRequired_ShowsOtpInput()
    {
        var authentication = new FakeAuthenticationService(LoginResult.Failure("Error_LoginTwoFactorRequired", true));
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = "alice";
        viewModel.Password = "password";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.True(viewModel.RequiresTwoFactor);
        Assert.Equal("Error_LoginTwoFactorRequired", viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData("Error_LoginRateLimited")]
    [InlineData("Error_LoginLocked")]
    [InlineData("Error_LoginProtectionUnavailable")]
    public async Task LoginAsync_IdentityProtectionError_PreservesResourceKey(string errorResourceKey)
    {
        var viewModel = CreateViewModel(new FakeAuthenticationService(LoginResult.Failure(errorResourceKey)));
        viewModel.LoginOrEmail = "alice";
        viewModel.Password = "password";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(errorResourceKey, viewModel.ErrorMessage);
    }

    [Fact]
    public void PasteOtpCodeCommand_SixDigits_FillsEveryInput()
    {
        var viewModel = CreateViewModel(new FakeAuthenticationService(LoginResult.Failure("Error_LoginTwoFactorRequired", true)));

        viewModel.PasteOtpCodeCommand.Execute("123456");

        Assert.Equal("123456", viewModel.OtpCode);
    }

    private static LoginViewModel CreateViewModel(IAuthenticationService authentication) =>
        new(authentication, new TestNavigationService(), new TestLocalizationService());

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private readonly LoginResult _result;

        public FakeAuthenticationService(LoginResult result)
        {
            _result = result;
        }

        public Task<LoginResult> LoginAsync(string loginOrEmail, string password, string otpCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);

        public Task<FastAuthSession?> CreateFastAuthSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<FastAuthSession?>(null);

        public async IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(FastAuthSession session, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<RegistrationStartResult> StartRegistrationAsync(string firstName, string lastName, string username, string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegistrationStartResult.Failure("Error_RegistrationFailed"));

        public Task<AuthenticationOperationResult> ConfirmRegistrationAsync(string codeId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Failure("Error_RegistrationCodeInvalid"));

        public Task<AuthenticationOperationResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Failure("Error_PasswordInvalid"));

        public Task<PasswordResetStartResult> StartPasswordResetAsync(string loginOrEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(PasswordResetStartResult.Failure("Error_PasswordResetFailed"));

        public Task<AuthenticationOperationResult> CompletePasswordResetAsync(string resetId, string code, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Failure("Error_PasswordResetCodeInvalid"));
    }

    private sealed class TestNavigationService : IOnboardingNavigationService
    {
        public object? CurrentViewModel => null;
        public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged
        {
            add { }
            remove { }
        }
        public void ShowWelcome() { }
        public void ShowSelectNode() { }
        public void ShowConnectedNode() { }
        public void ShowLogin() { }
        public void ShowRegistration() { }
        public void ShowPasswordRecovery() { }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string ResolveSupportedLanguage(string? requestedLanguage) => "en";
        public void Apply(string language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
