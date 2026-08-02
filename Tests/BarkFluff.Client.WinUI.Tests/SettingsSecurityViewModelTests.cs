using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels.Settings;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class SettingsSecurityViewModelTests
{
    [Fact]
    public async Task LoadAsync_AppliesServerStateWithoutStartingAFlow()
    {
        var service = new FakeSecuritySettingsService { AuthenticatorEnabled = true, EmailEnabled = true };
        var viewModel = CreateViewModel(service);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsAuthenticatorEnabled);
        Assert.True(viewModel.IsEmailEnabled);
        Assert.Equal(SecurityFlow.None, viewModel.Flow);
        Assert.Equal(0, service.SetupCalls);
        Assert.Equal(0, service.DisableCalls);
    }

    [Fact]
    public async Task EnablingAuthenticator_RequestsSetupAndShowsQr()
    {
        var service = new FakeSecuritySettingsService { QrBase64 = "qr", ManualCode = "ABCDEF" };
        var viewModel = CreateViewModel(service);

        viewModel.IsAuthenticatorEnabled = true;
        await service.PendingWork;

        Assert.Equal(SecurityFlow.TwoFactorSetup, viewModel.Flow);
        Assert.Equal("qr", viewModel.QrCode);
        Assert.Equal("ABCDEF", viewModel.ManualCode);
        Assert.True(viewModel.IsAuthenticatorSetup);
        Assert.Equal(TwoFactorMethod.Authenticator, service.LastSetupMethod);
    }

    [Fact]
    public async Task EnablingEmail_ShowsMailHintInsteadOfQr()
    {
        var service = new FakeSecuritySettingsService();
        var viewModel = CreateViewModel(service);

        viewModel.IsEmailEnabled = true;
        await service.PendingWork;

        Assert.Equal(SecurityFlow.TwoFactorSetup, viewModel.Flow);
        Assert.True(viewModel.IsEmailSetup);
        Assert.False(viewModel.IsAuthenticatorSetup);
        Assert.Equal(TwoFactorMethod.Email, service.LastSetupMethod);
    }

    [Fact]
    public async Task SetupFailure_RevertsSwitchWithoutStartingAnotherFlow()
    {
        var service = new FakeSecuritySettingsService { SetupErrorKey = "Error_TwoFactorFailed" };
        var viewModel = CreateViewModel(service);

        viewModel.IsAuthenticatorEnabled = true;
        await service.PendingWork;

        Assert.False(viewModel.IsAuthenticatorEnabled);
        Assert.Equal(SecurityFlow.None, viewModel.Flow);
        Assert.Equal("Error_TwoFactorFailed", viewModel.ErrorMessage);
        Assert.Equal(1, service.SetupCalls);
        Assert.Equal(0, service.DisableCalls);
    }

    [Fact]
    public async Task CancellingSetup_RevertsSwitchWithoutRestartingSetup()
    {
        var service = new FakeSecuritySettingsService();
        var viewModel = CreateViewModel(service);

        viewModel.IsAuthenticatorEnabled = true;
        await service.PendingWork;
        viewModel.CancelFlowCommand.Execute(null);

        Assert.False(viewModel.IsAuthenticatorEnabled);
        Assert.Equal(SecurityFlow.None, viewModel.Flow);
        Assert.Equal(1, service.SetupCalls);
    }

    [Fact]
    public async Task DisablingAuthenticator_AsksForCodeBeforeCallingTheServer()
    {
        var service = new FakeSecuritySettingsService { AuthenticatorEnabled = true };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync();

        viewModel.IsAuthenticatorEnabled = false;

        Assert.Equal(SecurityFlow.TwoFactorDisable, viewModel.Flow);
        Assert.Equal(0, service.DisableCalls);

        viewModel.ConfirmationCode = "123456";
        await viewModel.ConfirmTwoFactorCommand.ExecuteAsync(null);

        Assert.Equal(1, service.DisableCalls);
        Assert.Equal("123456", service.LastDisableCode);
        Assert.Equal(SecurityFlow.None, viewModel.Flow);
    }

    [Fact]
    public async Task DisablingEmail_CallsTheServerWithoutACode()
    {
        var service = new FakeSecuritySettingsService { EmailEnabled = true };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync();

        viewModel.IsEmailEnabled = false;
        await service.PendingWork;

        Assert.Equal(SecurityFlow.None, viewModel.Flow);
        Assert.Equal(1, service.DisableCalls);
        Assert.Equal(TwoFactorMethod.Email, service.LastDisableMethod);
        Assert.Equal(string.Empty, service.LastDisableCode);
    }

    [Fact]
    public async Task CancellingDisable_RestoresTheEnabledSwitch()
    {
        var service = new FakeSecuritySettingsService { AuthenticatorEnabled = true };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync();

        viewModel.IsAuthenticatorEnabled = false;
        viewModel.CancelFlowCommand.Execute(null);

        Assert.True(viewModel.IsAuthenticatorEnabled);
        Assert.Equal(SecurityFlow.None, viewModel.Flow);
        Assert.Equal(0, service.DisableCalls);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    public async Task ConfirmingWithAMalformedCode_DoesNotReachTheServer(string code)
    {
        var service = new FakeSecuritySettingsService();
        var viewModel = CreateViewModel(service);

        viewModel.IsAuthenticatorEnabled = true;
        await service.PendingWork;
        viewModel.ConfirmationCode = code;
        await viewModel.ConfirmTwoFactorCommand.ExecuteAsync(null);

        Assert.Equal(0, service.ConfirmCalls);
        Assert.Equal("Error_ConfirmationCodeFormat", viewModel.ErrorMessage);
        Assert.Equal(SecurityFlow.TwoFactorSetup, viewModel.Flow);
    }

    [Fact]
    public async Task PasswordChange_WalksThroughTheThreeSteps()
    {
        var service = new FakeSecuritySettingsService { ResetId = "reset-1" };
        var viewModel = CreateViewModel(service);

        viewModel.BeginPasswordChangeCommand.Execute(null);
        Assert.Equal(SecurityFlow.PasswordRequest, viewModel.Flow);

        await viewModel.RequestPasswordCodeCommand.ExecuteAsync(null);
        Assert.Equal(SecurityFlow.PasswordCode, viewModel.Flow);

        viewModel.ConfirmationCode = "654321";
        await viewModel.ConfirmPasswordCodeCommand.ExecuteAsync(null);
        Assert.Equal(SecurityFlow.PasswordNew, viewModel.Flow);
        Assert.Equal("reset-1", service.LastResetId);

        viewModel.NewPassword = "secret1";
        viewModel.RepeatPassword = "secret1";
        await viewModel.SavePasswordCommand.ExecuteAsync(null);

        Assert.Equal(SecurityFlow.None, viewModel.Flow);
        Assert.Equal("secret1", service.LastPassword);
        Assert.Equal("Settings_Security_PasswordChanged", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SavingAShortPassword_DoesNotReachTheServer()
    {
        var service = new FakeSecuritySettingsService();
        var viewModel = CreateViewModel(service);
        await ReachPasswordStepAsync(viewModel);

        viewModel.NewPassword = "short";
        viewModel.RepeatPassword = "short";
        await viewModel.SavePasswordCommand.ExecuteAsync(null);

        Assert.Null(service.LastPassword);
        Assert.Equal("Error_PasswordTooShort", viewModel.ErrorMessage);
        Assert.Equal(SecurityFlow.PasswordNew, viewModel.Flow);
    }

    [Fact]
    public async Task SavingMismatchedPasswords_DoesNotReachTheServer()
    {
        var service = new FakeSecuritySettingsService();
        var viewModel = CreateViewModel(service);
        await ReachPasswordStepAsync(viewModel);

        viewModel.NewPassword = "secret1";
        viewModel.RepeatPassword = "secret2";
        await viewModel.SavePasswordCommand.ExecuteAsync(null);

        Assert.Null(service.LastPassword);
        Assert.Equal("Error_PasswordMismatch", viewModel.ErrorMessage);
    }

    private static async Task ReachPasswordStepAsync(SettingsSecurityViewModel viewModel)
    {
        viewModel.BeginPasswordChangeCommand.Execute(null);
        await viewModel.RequestPasswordCodeCommand.ExecuteAsync(null);
        viewModel.ConfirmationCode = "654321";
        await viewModel.ConfirmPasswordCodeCommand.ExecuteAsync(null);
    }

    private static SettingsSecurityViewModel CreateViewModel(FakeSecuritySettingsService service) =>
        new(service, new StubLocalizationService());
}

/// <summary>
/// Все методы завершаются синхронно, поэтому «выстрелил и забыл» в сеттерах переключателей
/// успевает отработать до проверок; <see cref="PendingWork"/> сохраняет это явным.
/// </summary>
internal sealed class FakeSecuritySettingsService : ISecuritySettingsService
{
    public bool AuthenticatorEnabled { get; set; }

    public bool EmailEnabled { get; set; }

    public string QrBase64 { get; set; } = string.Empty;

    public string ManualCode { get; set; } = string.Empty;

    public string ResetId { get; set; } = "reset";

    public string? SetupErrorKey { get; set; }

    public string? DisableErrorKey { get; set; }

    public int SetupCalls { get; private set; }

    public int ConfirmCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public TwoFactorMethod? LastSetupMethod { get; private set; }

    public TwoFactorMethod? LastDisableMethod { get; private set; }

    public string? LastDisableCode { get; private set; }

    public string? LastResetId { get; private set; }

    public string? LastPassword { get; private set; }

    public Task PendingWork { get; private set; } = Task.CompletedTask;

    public Task<(string? ErrorKey, bool AuthenticatorEnabled, bool EmailEnabled)> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Track(Task.FromResult<(string?, bool, bool)>((null, AuthenticatorEnabled, EmailEnabled)));

    public Task<(string? ErrorKey, string QrBase64, string ManualCode)> BeginTwoFactorSetupAsync(TwoFactorMethod method, CancellationToken cancellationToken = default)
    {
        SetupCalls++;
        LastSetupMethod = method;
        return Track(Task.FromResult<(string?, string, string)>((SetupErrorKey, QrBase64, ManualCode)));
    }

    public Task<string?> ConfirmTwoFactorAsync(string code, CancellationToken cancellationToken = default)
    {
        ConfirmCalls++;
        return Track(Task.FromResult<string?>(null));
    }

    public Task<string?> DisableTwoFactorAsync(TwoFactorMethod method, string code, CancellationToken cancellationToken = default)
    {
        DisableCalls++;
        LastDisableMethod = method;
        LastDisableCode = code;
        return Track(Task.FromResult(DisableErrorKey));
    }

    public Task<(string? ErrorKey, string ResetId)> RequestPasswordCodeAsync(CancellationToken cancellationToken = default) =>
        Track(Task.FromResult<(string?, string)>((null, ResetId)));

    public Task<string?> ConfirmPasswordCodeAsync(string resetId, string code, CancellationToken cancellationToken = default)
    {
        LastResetId = resetId;
        return Track(Task.FromResult<string?>(null));
    }

    public Task<string?> SetPasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        LastPassword = newPassword;
        return Track(Task.FromResult<string?>(null));
    }

    private Task<T> Track<T>(Task<T> task)
    {
        PendingWork = task;
        return task;
    }
}
