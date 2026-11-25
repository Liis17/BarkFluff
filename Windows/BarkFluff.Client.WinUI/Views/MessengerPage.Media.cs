using System.ComponentModel;
using BarkFluff.Client.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private ChatMediaViewer? _mediaViewer;
    private BitmapImage? _mediaBitmap;
    private MediaPlayer? _mediaPlayer;
    private Uri? _displayedMediaUri;
    private Button? _mediaOpener;
    private bool _pickingMediaDestination;
    private DispatcherTimer? _mediaSaveStatusTimer;

    private void BindMediaViewer(ChatMediaViewer? viewer)
    {
        if (_mediaViewer is not null)
        {
            _mediaViewer.Close();
            _mediaViewer.PropertyChanged -= OnMediaViewerChanged;
        }

        _mediaViewer = viewer;
        if (viewer is not null)
            viewer.PropertyChanged += OnMediaViewerChanged;
    }

    private async void OnOpenMediaClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: MessageAttachmentItemViewModel attachment } button)
        {
            _mediaOpener = button;
            await ViewModel.OpenMediaAsync(attachment);
            if (!ViewModel.MediaViewer.IsOpen)
                _mediaOpener = null;
        }
    }

    private void OnMediaViewerChanged(object? sender, PropertyChangedEventArgs args)
    {
        var viewer = (ChatMediaViewer)sender!;
        if (args.PropertyName == nameof(ChatMediaViewer.IsOpen))
        {
            MediaOverlay.Visibility = viewer.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (viewer.IsOpen)
            {
                MediaCloseButton.Focus(FocusState.Programmatic);
            }
            else
            {
                if (_mediaOpener?.Focus(FocusState.Programmatic) != true)
                    SidebarSearchBox.Focus(FocusState.Programmatic);
                _mediaOpener = null;
            }
        }

        if (args.PropertyName == nameof(ChatMediaViewer.SaveStatus))
            ShowMediaSaveStatus(viewer.SaveStatus);

        if (args.PropertyName == nameof(ChatMediaViewer.Source))
        {
            if (viewer.IsOpen && viewer.Source is null)
                MediaCloseButton.Focus(FocusState.Programmatic);
            try
            {
                SetMediaSource(viewer.Source, viewer.Current?.IsVideo == true);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (viewer.IsOpen)
                    viewer.ReportOpenError();
            }
        }
    }

    private void ShowMediaSaveStatus(string? status)
    {
        _mediaSaveStatusTimer?.Stop();
        MediaSaveStatus.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        if (status is null)
            return;

        if (_mediaSaveStatusTimer is null)
        {
            _mediaSaveStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _mediaSaveStatusTimer.Tick += (_, _) => ShowMediaSaveStatus(null);
        }
        _mediaSaveStatusTimer.Start();
    }

    private void SetMediaSource(Uri? uri, bool isVideo)
    {
        _displayedMediaUri = null;
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.MediaFailed -= OnVideoFailed;
            var source = MediaVideo.Source as MediaSource;
            if (_mediaPlayer.PlaybackSession.CanPause)
                _mediaPlayer.Pause();
            MediaVideo.Source = null;
            MediaVideo.SetMediaPlayer(null);
            source?.Dispose();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }
        if (_mediaBitmap is not null)
        {
            _mediaBitmap.ImageFailed -= OnMediaImageFailed;
            MediaImage.Source = null;
            _mediaBitmap = null;
        }

        MediaVideo.Visibility = uri is not null && isVideo ? Visibility.Visible : Visibility.Collapsed;
        MediaZoom.Visibility = uri is not null && !isVideo ? Visibility.Visible : Visibility.Collapsed;
        if (uri is null)
            return;

        _displayedMediaUri = uri;
        if (isVideo)
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaFailed += OnVideoFailed;
            MediaVideo.SetMediaPlayer(_mediaPlayer);
            MediaVideo.Source = MediaSource.CreateFromUri(uri);
            _mediaPlayer.Play();
        }
        else
        {
            MediaZoom.ChangeView(0, 0, 1, disableAnimation: true);
            _mediaBitmap = new BitmapImage();
            _mediaBitmap.ImageFailed += OnMediaImageFailed;
            MediaImage.Source = _mediaBitmap;
            _mediaBitmap.UriSource = uri;
        }
    }

    private void OnVideoFailed(MediaPlayer player, MediaPlayerFailedEventArgs args) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (player == _mediaPlayer && _displayedMediaUri is { } uri && _mediaViewer is { } viewer)
                await viewer.RefreshFailedSourceAsync(uri);
        });

    private void OnMediaImageFailed(object sender, ExceptionRoutedEventArgs args) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (sender is BitmapImage bitmap && bitmap == _mediaBitmap && _displayedMediaUri is { } uri && _mediaViewer is { } viewer)
                await viewer.RefreshFailedSourceAsync(uri);
        });

    private void OnCloseMediaClick(object sender, RoutedEventArgs args) => ViewModel.MediaViewer.Close();

    private async void OnPreviousMediaClick(object sender, RoutedEventArgs args) => await NavigateMediaAsync(-1);

    private async void OnNextMediaClick(object sender, RoutedEventArgs args) => await NavigateMediaAsync(1);

    private Task NavigateMediaAsync(int direction)
    {
        if (_mediaViewer is not { IsOpen: true } viewer ||
            !(direction < 0 ? viewer.CanGoPrevious : viewer.CanGoNext))
            return Task.CompletedTask;

        MediaCloseButton.Focus(FocusState.Programmatic);
        return viewer.NavigateAsync(direction);
    }

    private async void OnMediaArrow(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_mediaViewer is not { IsOpen: true } ||
            FocusManager.GetFocusedElement(XamlRoot) is Slider)
            return;

        args.Handled = true;
        await NavigateMediaAsync(sender.Key == Windows.System.VirtualKey.Left ? -1 : 1);
    }

    private async void OnSaveMediaClick(object sender, RoutedEventArgs args)
    {
        if (_pickingMediaDestination || _mediaViewer is not { Current: { } item } viewer)
            return;

        _pickingMediaDestination = true;
        MediaCloseButton.Focus(FocusState.Programmatic);
        MediaSaveButton.IsEnabled = false;
        try
        {
            await viewer.SaveAsync(item, viewer.Lifetime, async (extension, copy, token) =>
            {
                var name = Path.GetFileName(item.FileName);
                var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name) };
                picker.FileTypeChoices.Add(ResourceString("Messenger_MediaFile"), [extension]);
                var window = App.Services.GetRequiredService<MainWindow>();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
                var file = await picker.PickSaveFileAsync().AsTask(token);
                if (file is null)
                    return false;

                await WriteMediaAsync(file, copy, token);
                return true;
            });
        }
        finally
        {
            _pickingMediaDestination = false;
            MediaSaveButton.IsEnabled = true;
        }
    }

    private static async Task WriteMediaAsync(StorageFile file, Func<Stream, Task> copy, CancellationToken token)
    {
        try
        {
            using var transaction = await file.OpenTransactedWriteAsync().AsTask(token);
            transaction.Stream.Size = 0;
            using var output = transaction.Stream.AsStreamForWrite();
            await copy(output);
            await output.FlushAsync(token);
            token.ThrowIfCancellationRequested();
            await transaction.CommitAsync();
        }
        catch
        {
            try
            {
                if ((await file.GetBasicPropertiesAsync()).Size == 0)
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch (Exception) { }
            throw;
        }
    }
}
