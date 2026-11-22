using BarkFluff.Client.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private bool _pickingAttachments;

    private bool CanBeginAttachmentInput(bool canAttach, bool sending) => canAttach && !sending;

    private async void OnAttachClick(object sender, RoutedEventArgs args)
    {
        if (_pickingAttachments)
            return;
        _pickingAttachments = true;
        try
        {
            await ViewModel.AddAttachmentsAsync(async token =>
            {
                var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
                picker.FileTypeFilter.Add("*");
                var window = App.Services.GetRequiredService<MainWindow>();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
                var files = await picker.PickMultipleFilesAsync().AsTask(token);
                return GetAttachmentFiles(files);
            });
        }
        finally
        {
            _pickingAttachments = false;
        }
    }

    private async void OnComposerPaste(object sender, TextControlPasteEventArgs args)
    {
        DataPackageView data;
        try
        {
            data = Clipboard.GetContent();
            if (!CanBeginAttachmentInput(ViewModel.CanAttach, ViewModel.AttachmentComposer.IsSending) &&
                data.Contains(StandardDataFormats.Text))
                return;
            if (!data.Contains(StandardDataFormats.StorageItems) &&
                (data.Contains(StandardDataFormats.Text) || !data.Contains(StandardDataFormats.Bitmap)))
                return;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }

        args.Handled = true;
        await ViewModel.AddAttachmentsAsync(token => ReadAttachmentDataAsync(data, token));
    }

    private void OnAttachmentDragOver(object sender, DragEventArgs args)
    {
        if (!HasAttachmentData(args.DataView))
            return;

        var accepts = ViewModel is { CanAttach: true } && !ViewModel.AttachmentComposer.IsSending;
        args.AcceptedOperation = accepts ? DataPackageOperation.Copy : DataPackageOperation.None;
        SetAttachmentDropHighlight(accepts);
        if (accepts)
            args.DragUIOverride.Caption = (string)Application.Current.Resources["Messenger_AttachmentDrop"];
        args.Handled = true;
    }

    private void OnAttachmentDragLeave(object sender, DragEventArgs args) => SetAttachmentDropHighlight(false);

    private async void OnAttachmentDrop(object sender, DragEventArgs args)
    {
        SetAttachmentDropHighlight(false);
        if (!HasAttachmentData(args.DataView))
            return;

        args.Handled = true;
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.AddAttachmentsAsync(token => ReadAttachmentDataAsync(args.DataView, token));
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void SetAttachmentDropHighlight(bool visible) =>
        AttachmentDropHighlight.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static bool HasAttachmentData(DataPackageView data) =>
        data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Bitmap);

    private static AttachmentSourceFile[] GetAttachmentFiles(IEnumerable<IStorageItem> items) =>
        items.OfType<StorageFile>().Where(file => !string.IsNullOrEmpty(file.Path))
            .Take(AttachmentComposer.MaxFiles + 1)
            .Select(file => new AttachmentSourceFile(file.Path, file.Name, file.ContentType)).ToArray();

    private static async Task<IReadOnlyList<AttachmentSourceFile>> ReadAttachmentDataAsync(DataPackageView data, CancellationToken token)
    {
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var files = GetAttachmentFiles(await data.GetStorageItemsAsync().AsTask(token));
            if (files.Length != 0)
                return files;
        }

        if (!data.Contains(StandardDataFormats.Bitmap))
            throw new InvalidDataException();

        var bitmap = await data.GetBitmapAsync().AsTask(token);
        using var source = await bitmap.OpenReadAsync().AsTask(token);
        var decoder = await BitmapDecoder.CreateAsync(source).AsTask(token);
        using var decoded = await decoder.GetSoftwareBitmapAsync().AsTask(token);
        using var png = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png).AsTask(token);
        encoder.SetSoftwareBitmap(decoded);
        await encoder.FlushAsync().AsTask(token);

        var path = Path.Combine(Path.GetTempPath(), $"barkfluff-{Guid.NewGuid():N}.png");
        try
        {
            png.Seek(0);
            using var input = png.AsStreamForRead();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await input.CopyToAsync(output, token);
            var name = (string)Application.Current.Resources["Messenger_AttachmentClipboardName"];
            return [new AttachmentSourceFile(path, name, "image/png", IsTemporary: true)];
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private async void OnAttachmentThumbnailLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Image { DataContext: PendingAttachment { HasThumbnail: true } attachment } image)
            return;

        try
        {
            var source = new BitmapImage { DecodePixelWidth = 56, AutoPlay = false };
            if (attachment.HasVideoPreview)
            {
                var video = await StorageFile.GetFileFromPathAsync(attachment.Path);
                using var thumbnail = await video.GetThumbnailAsync(ThumbnailMode.VideosView, 56);
                if (thumbnail is not { Type: ThumbnailType.Image })
                    return;
                await source.SetSourceAsync(thumbnail);
            }
            else
            {
                // Разрешаем очистку временного файла, пока декодируется миниатюра.
                await using var file = await Task.Run(() => new FileStream(attachment.Path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, bufferSize: 81920, useAsync: true));
                using var stream = file.AsRandomAccessStream();
                await source.SetSourceAsync(stream);
            }
            if (ReferenceEquals(image.DataContext, attachment))
                image.Source = source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Имя и тип остаются доступны, даже если локальный файл больше не открывается.
        }
    }

    private async void OnRetryAttachmentClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PendingAttachment attachment })
            await ViewModel.RetryAttachmentAsync(attachment);
    }
}
