using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;

using System.Runtime.CompilerServices;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class AuthenticationViewModelTests
{
    [Fact]
    public async Task LoadFastAuthAsync_Accepted_ShowsSuccessfulSignIn()
    {
        var authentication = new FakeAuthenticationService
        {
            FastAuthSession = new FastAuthSession(
                "session",
                new FastAuthQrCode(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9JfWQAAAAASUVORK5CYII=",
                    DateTimeOffset.UtcNow.AddMinutes(5))),
            FastAuthUpdates = [new FastAuthUpdate(FastAuthUpdateKind.Accepted)]
        };
        var viewModel = new LoginViewModel(authentication, new TestNavigationService(), new TestLocalizationService());

        await viewModel.LoadFastAuthAsync();

        Assert.Equal("Login_Success", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadFastAuthAsync_Expired_CreatesAndWaitsForANewQrSession()
    {
        var authentication = new FakeAuthenticationService();
        authentication.FastAuthSessions.Enqueue(CreateFastAuthSession("first"));
        authentication.FastAuthSessions.Enqueue(CreateFastAuthSession("second"));
        authentication.FastAuthUpdateSequences.Enqueue([new FastAuthUpdate(FastAuthUpdateKind.Expired)]);
        authentication.FastAuthUpdateSequences.Enqueue([new FastAuthUpdate(FastAuthUpdateKind.Accepted)]);
        var viewModel = new LoginViewModel(authentication, new TestNavigationService(), new TestLocalizationService());

        await viewModel.LoadFastAuthAsync();

        Assert.Equal(2, authentication.FastAuthSessionCalls);
        Assert.Equal("Login_Success", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RegistrationFlow_ConfirmationThenPassword_Completes()
    {
        var authentication = new FakeAuthenticationService
        {
            RegistrationStart = RegistrationStartResult.Success("code-id")
        };
        var viewModel = new RegistrationViewModel(authentication, new TestNavigationService(), new TestLocalizationService())
        {
            FirstName = "Alice",
            Username = "alice",
            Email = "alice@example.com"
        };

        await viewModel.CreateAccountCommand.ExecuteAsync(null);
        viewModel.ConfirmationCode = "123456";
        await viewModel.ConfirmAccountCommand.ExecuteAsync(null);
        viewModel.Password = "password";
        viewModel.PasswordConfirmation = "password";
        await viewModel.SetPasswordCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Step);
        Assert.Equal("Registration_Success", viewModel.StatusMessage);
        Assert.Equal(1, authentication.SetPasswordCalls);
    }

    [Fact]
    public async Task PasswordRecoveryFlow_CompletesAndClearsSecrets()
    {
        var authentication = new FakeAuthenticationService
        {
            PasswordResetStart = PasswordResetStartResult.Success("reset-id")
        };
        var viewModel = new PasswordRecoveryViewModel(authentication, new TestNavigationService(), new TestLocalizationService())
        {
            LoginOrEmail = "alice@example.com"
        };

        await viewModel.RequestCodeCommand.ExecuteAsync(null);
        viewModel.ConfirmationCode = "123456";
        viewModel.Password = "password";
        viewModel.PasswordConfirmation = "password";
        await viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.Equal("PasswordRecovery_Success", viewModel.StatusMessage);
        Assert.Empty(viewModel.Password);
        Assert.Empty(viewModel.PasswordConfirmation);
        Assert.Empty(viewModel.ConfirmationCode);
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public FastAuthSession? FastAuthSession { get; init; }
        public IReadOnlyList<FastAuthUpdate> FastAuthUpdates { get; init; } = [];
        public RegistrationStartResult RegistrationStart { get; init; } = RegistrationStartResult.Failure("Error_RegistrationFailed");
        public PasswordResetStartResult PasswordResetStart { get; init; } = PasswordResetStartResult.Failure("Error_PasswordResetFailed");
        public int SetPasswordCalls { get; private set; }
        public int FastAuthSessionCalls { get; private set; }
        public Queue<FastAuthSession?> FastAuthSessions { get; } = [];
        public Queue<IReadOnlyList<FastAuthUpdate>> FastAuthUpdateSequences { get; } = [];

        public Task<LoginResult> LoginAsync(string loginOrEmail, string password, string otpCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LoginResult.Success());

        public Task<FastAuthSession?> CreateFastAuthSessionAsync(CancellationToken cancellationToken = default)
        {
            FastAuthSessionCalls++;
            return Task.FromResult(FastAuthSessions.Count > 0 ? FastAuthSessions.Dequeue() : FastAuthSession);
        }

        public async IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(FastAuthSession session, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var updates = FastAuthUpdateSequences.Count > 0 ? FastAuthUpdateSequences.Dequeue() : FastAuthUpdates;
            foreach (var update in updates)
            {
                yield return update;
            }

            await Task.CompletedTask;
        }

        public Task<RegistrationStartResult> StartRegistrationAsync(string firstName, string lastName, string username, string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegistrationStart);

        public Task<AuthenticationOperationResult> ConfirmRegistrationAsync(string codeId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Success());

        public Task<AuthenticationOperationResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default)
        {
            SetPasswordCalls++;
            return Task.FromResult(AuthenticationOperationResult.Success());
        }

        public Task<PasswordResetStartResult> StartPasswordResetAsync(string loginOrEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(PasswordResetStart);

        public Task<AuthenticationOperationResult> CompletePasswordResetAsync(string resetId, string code, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Success());
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

    private static FastAuthSession CreateFastAuthSession(string id) => new(
        id,
        new FastAuthQrCode(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9JfWQAAAAASUVORK5CYII=",
            DateTimeOffset.UtcNow.AddMinutes(5)));
}
