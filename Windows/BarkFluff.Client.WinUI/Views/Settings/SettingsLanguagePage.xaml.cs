using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsLanguagePage : Page
{
    public SettingsLanguagePage() => InitializeComponent();

    public SettingsLanguageViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsLanguageViewModel>();
        await ViewModel.LoadAsync();
        SystemLanguageRadio.IsChecked = ViewModel.SelectedLanguage == "system";
        RussianLanguageRadio.IsChecked = ViewModel.SelectedLanguage == "ru";
        EnglishLanguageRadio.IsChecked = ViewModel.SelectedLanguage == "en";
    }

    private async void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string language)
        {
            await ViewModel.SelectAsync(language);
        }
    }
}
