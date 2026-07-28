using BarkFluff.ClientV2.WPF.ViewModels;

namespace BarkFluff.ClientV2.WPF.Services;

public interface IOnboardingNavigationService
{
    event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged;

    void ShowWelcome();

    void ShowSelectNode();

    void ShowConnectedNode();
}

public sealed class OnboardingNavigationEventArgs : EventArgs
{
    public OnboardingNavigationEventArgs(object viewModel)
    {
        ViewModel = viewModel;
    }

    public object ViewModel { get; }
}
