using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.Core.Services;

public interface IOnboardingNavigationService
{
    object? CurrentViewModel { get; }

    event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged;

    void ShowWelcome();

    void ShowSelectNode();

    void ShowConnectedNode();

    void ShowLogin();

    void ShowRegistration();

    void ShowPasswordRecovery();

    void ShowMessenger() { }
}

public sealed class OnboardingNavigationEventArgs : EventArgs
{
    public OnboardingNavigationEventArgs(object viewModel)
    {
        ViewModel = viewModel;
    }

    public object ViewModel { get; }
}
