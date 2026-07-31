using Microsoft.Extensions.DependencyInjection;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Storage.Pickers;

namespace BarkFluff.Client.WinUI.Views.Controls;

/// <summary>
/// Выбор изображения перед загрузкой: файл, предпросмотр и переход дальше.
/// </summary>
/// <remarks>
/// Заготовка под кроппер. Пока «Далее» отдаёт файл как есть, поэтому кадрирование Android
/// (1:1 для аватара, 3:1 для постера) не воспроизводится — изображение уходит целиком.
/// </remarks>
public sealed partial class ImagePickDialog : ContentDialog
{
    public ImagePickDialog() => InitializeComponent();

    public string? SelectedPath { get; private set; }

    private async void OnChooseFileClick(object sender, RoutedEventArgs eventArgs)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");

        // Диалогу выбора файла в WinUI нужен владелец: без дескриптора окна он не открывается.
        var window = App.Services.GetRequiredService<MainWindow>();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        SelectedPath = file.Path;
        Preview.Source = new BitmapImage(new Uri(file.Path));
        Preview.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = true;
    }
}
