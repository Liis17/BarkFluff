using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

using System.ComponentModel;

using Windows.System;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage() => InitializeComponent();

    public LoginViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        ViewModel = (LoginViewModel)e.Parameter;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Bindings.Update();
        DispatcherQueue.TryEnqueue(FocusCurrentStep);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LoginViewModel.Step) or nameof(LoginViewModel.IsIdentifierStep)
            or nameof(LoginViewModel.IsPasswordStep) or nameof(LoginViewModel.IsChallengeStep))
        {
            DispatcherQueue.TryEnqueue(FocusCurrentStep);
        }
    }

    private async void OnLoginKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!ViewModel.LoginCommand.CanExecute(null)) return;
        await ViewModel.LoginCommand.ExecuteAsync(null);
        FocusCurrentStep();
    }

    private async void OnOpenTelegramClick(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(ViewModel.TelegramUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host is not ("t.me" or "telegram.me"))
        {
            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception)
        {
        }
    }

    private void FocusCurrentStep()
    {
        if (ViewModel.IsIdentifierStep)
        {
            IdentifierInput.Focus(FocusState.Programmatic);
        }
        else if (ViewModel.IsPasswordStep)
        {
            PasswordInput.Focus(FocusState.Programmatic);
        }
        else if (ViewModel.RequiresTwoFactor)
        {
            CodeInput.Focus(FocusState.Programmatic);
        }
    }
}
