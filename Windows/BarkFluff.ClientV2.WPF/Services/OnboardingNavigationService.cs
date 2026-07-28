using BarkFluff.ClientV2.WPF.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace BarkFluff.ClientV2.WPF.Services;

public sealed class OnboardingNavigationService : IOnboardingNavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public OnboardingNavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged;

    public void ShowWelcome() => Navigate(_serviceProvider.GetRequiredService<WelcomeViewModel>());

    public void ShowSelectNode()
    {
        var viewModel = _serviceProvider.GetRequiredService<SelectNodeViewModel>();
        _ = viewModel.LoadPublicNodesAsync();
        Navigate(viewModel);
    }

    public void ShowConnectedNode() => Navigate(_serviceProvider.GetRequiredService<ConnectedNodeViewModel>());

    private void Navigate(object viewModel)
    {
        CurrentViewModelChanged?.Invoke(this, new OnboardingNavigationEventArgs(viewModel));
    }
}
