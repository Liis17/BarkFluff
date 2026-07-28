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

    public object? CurrentViewModel { get; private set; }

    public void ShowWelcome() => Navigate(_serviceProvider.GetRequiredService<WelcomeViewModel>());

    public void ShowSelectNode()
    {
        var viewModel = _serviceProvider.GetRequiredService<SelectNodeViewModel>();
        _ = viewModel.LoadPublicNodesAsync();
        Navigate(viewModel);
    }

    public void ShowConnectedNode() => Navigate(_serviceProvider.GetRequiredService<ConnectedNodeViewModel>());

    public void ShowLogin()
    {
        var viewModel = _serviceProvider.GetRequiredService<LoginViewModel>();
        _ = viewModel.LoadFastAuthAsync();
        Navigate(viewModel);
    }

    public void ShowRegistration()
    {
        var viewModel = _serviceProvider.GetRequiredService<RegistrationViewModel>();
        viewModel.Reset();
        Navigate(viewModel);
    }

    public void ShowPasswordRecovery()
    {
        var viewModel = _serviceProvider.GetRequiredService<PasswordRecoveryViewModel>();
        viewModel.Reset();
        Navigate(viewModel);
    }

    private void Navigate(object viewModel)
    {
        if (CurrentViewModel is LoginViewModel loginViewModel && viewModel is not LoginViewModel)
        {
            loginViewModel.StopFastAuth();
            loginViewModel.ClearSensitiveState();
        }

        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, new OnboardingNavigationEventArgs(viewModel));
    }
}
