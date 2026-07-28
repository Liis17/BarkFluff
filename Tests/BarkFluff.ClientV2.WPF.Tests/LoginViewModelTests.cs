using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;

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

        public Task<FastAuthQrCode?> CreateFastAuthQrCodeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<FastAuthQrCode?>(null);
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
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string ResolveSupportedLanguage(string? requestedLanguage) => "en";
        public void Apply(string language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
