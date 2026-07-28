using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_WhenNavigationHappenedBeforeWindowCreation_ShowsCurrentViewModel()
    {
        var navigation = new RecordingNavigationService();
        var expectedViewModel = new object();
        navigation.Navigate(expectedViewModel);

        var viewModel = new MainWindowViewModel(navigation);

        Assert.Same(expectedViewModel, viewModel.CurrentViewModel);
    }

    private sealed class RecordingNavigationService : IOnboardingNavigationService
    {
        public object? CurrentViewModel { get; private set; }

        public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged;

        public void ShowWelcome() => Navigate(new object());
        public void ShowSelectNode() => Navigate(new object());
        public void ShowConnectedNode() => Navigate(new object());

        public void ShowLogin() => Navigate(new object());

        public void Navigate(object viewModel)
        {
            CurrentViewModel = viewModel;
            CurrentViewModelChanged?.Invoke(this, new OnboardingNavigationEventArgs(viewModel));
        }
    }
}
