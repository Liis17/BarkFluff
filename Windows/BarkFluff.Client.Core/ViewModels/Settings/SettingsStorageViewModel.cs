using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Files;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsStorageViewModel(
    IUserPreferencesService service,
    ILocalizationService localization) : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private double _usedPercentage;

    [ObservableProperty]
    private string _usedStorage = string.Empty;

    [ObservableProperty]
    private string _imagesStorage = string.Empty;

    [ObservableProperty]
    private string _videosStorage = string.Empty;

    [ObservableProperty]
    private string _audioStorage = string.Empty;

    [ObservableProperty]
    private string _documentsStorage = string.Empty;

    [ObservableProperty]
    private string _stickersStorage = string.Empty;

    [ObservableProperty]
    private string _avatarsStorage = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        var (errorKey, storage) = await service.GetUserStorageInfoAsync(cancellationToken);
        IsBusy = false;

        if (errorKey is not null || storage is null)
        {
            ErrorMessage = localization.GetString(errorKey ?? "Error_SettingsLoadFailed");
            return;
        }

        ErrorMessage = null;
        UsedPercentage = storage.LimitBytes > 0
            ? Math.Clamp((double)storage.UsedBytes / storage.LimitBytes * 100, 0, 100)
            : 0;
        UsedStorage = $"{FormatBytes(storage.UsedBytes)} {string.Format(localization.GetString("Settings_Storage_OfLimit"), FormatBytes(storage.LimitBytes))}";
        ImagesStorage = FormatBytes(GetUsage(storage, UploadFileType.MessageAttachmentImage, UploadFileType.MessageAttachmentGif));
        VideosStorage = FormatBytes(GetUsage(storage, UploadFileType.MessageAttachmentVideo));
        AudioStorage = FormatBytes(GetUsage(storage, UploadFileType.MessageAttachmentAudio, UploadFileType.MessageAttachmentVoice));
        DocumentsStorage = FormatBytes(GetUsage(storage, UploadFileType.MessageAttachmentDocument));
        StickersStorage = FormatBytes(GetUsage(storage, UploadFileType.MessageAttachmentSticker));
        AvatarsStorage = FormatBytes(GetUsage(storage, UploadFileType.UserAvatar, UploadFileType.ChatPicture, UploadFileType.UserProfilePoster));
    }

    private static long GetUsage(UserStorageInfo storage, params UploadFileType[] types) =>
        types.Sum(type => storage.UsageByType.GetValueOrDefault(type));

    private static string FormatBytes(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        const double gigabyte = megabyte * 1024d;
        return bytes >= gigabyte
            ? (bytes / gigabyte).ToString("0.##", CultureInfo.InvariantCulture) + " GB"
            : (bytes / megabyte).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
    }
}
