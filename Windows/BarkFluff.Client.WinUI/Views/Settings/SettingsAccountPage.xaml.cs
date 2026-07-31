using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Client.WinUI.Views.Controls;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views.Settings;

public sealed partial class SettingsAccountPage : Page
{
    public SettingsAccountPage() => InitializeComponent();

    public SettingsAccountViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<SettingsAccountViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync();
    }

    /// <summary>Выбор файла — дело представления, поэтому диалог открывается отсюда, а не командой.</summary>
    private async void OnChangePhotoClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new ImagePickDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SelectedPath is { Length: > 0 } path)
        {
            await ViewModel.UploadAvatarAsync(path);
        }
    }
}
