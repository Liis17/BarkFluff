using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;

namespace BarkFluff.Client.Core.ViewModels;

/// <summary>
/// Одна вкладка вложений в профиле (Фото/Видео/Файлы/Голосовые). Грузится лениво —
/// только когда вкладку выбрали, а не все четыре сразу при открытии профиля.
/// </summary>
public sealed partial class ProfileAttachmentsTabViewModel : ObservableObject
{
    private const int PageSize = 30;

    private readonly IMessengerService _messenger;
    private readonly ILocalizationService _localization;
    private readonly MessageAttachmentType _attachmentType;
    private string _chatId = string.Empty;
    private bool _isLoaded;
    private bool _hasReceivedResult;

    public ProfileAttachmentsTabViewModel(IMessengerService messenger, ILocalizationService localization, MessageAttachmentType attachmentType)
    {
        _messenger = messenger;
        _localization = localization;
        _attachmentType = attachmentType;
    }

    public ObservableCollection<MessageAttachmentItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _errorMessage;

    public bool HasMore => Items.Count < TotalCount;
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !HasItems && !IsLoading;

    /// <summary>Сбрасывает вкладку на новый чат; сама загрузка запускается отдельно через <see cref="EnsureLoadedAsync"/>.</summary>
    public void Reset(string? chatId)
    {
        _chatId = chatId ?? string.Empty;
        _isLoaded = false;
        _hasReceivedResult = false;
        Items.Clear();
        TotalCount = 0;
        ErrorMessage = null;
        RaiseCollectionDependentProperties();
    }

    public Task EnsureLoadedAsync()
    {
        if (_isLoaded || string.IsNullOrEmpty(_chatId))
        {
            return Task.CompletedTask;
        }

        _isLoaded = true;
        return LoadMoreAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        // HasMore до первого ответа сервера ложно-false (0 < 0), поэтому страж от повторной подгрузки
        // после исчерпания списка смотрит на _hasReceivedResult, а не просто на HasMore.
        if (IsLoading || string.IsNullOrEmpty(_chatId) || (_hasReceivedResult && !HasMore))
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var (error, attachments, totalCount) = await _messenger.ListChatAttachmentsAsync(_chatId, _attachmentType, Items.Count, PageSize);
            if (!error.IsSuccess || attachments is null)
            {
                ErrorMessage = _localization.GetString("Error_AttachmentsLoadFailed");
                return;
            }

            TotalCount = totalCount;
            _hasReceivedResult = true;
            foreach (var info in attachments)
            {
                Items.Add(await CreateItemAsync(info.Attachment));
            }
        }
        finally
        {
            IsLoading = false;
            RaiseCollectionDependentProperties();
        }
    }

    private async Task<MessageAttachmentItemViewModel> CreateItemAsync(MessageAttachment attachment)
    {
        var previewUrl = attachment.PreviewUrl;
        if (string.IsNullOrWhiteSpace(previewUrl)
            && attachment.Type is MessageAttachmentType.Image or MessageAttachmentType.Video or MessageAttachmentType.Gif or MessageAttachmentType.Sticker)
        {
            previewUrl = await ResolveFileUrlAsync(attachment.PreviewFileId);
        }

        var fileUrl = await ResolveFileUrlAsync(attachment.FileId);
        return new MessageAttachmentItemViewModel(
            attachment.Type,
            previewUrl,
            fileUrl,
            string.IsNullOrWhiteSpace(attachment.FileName) ? _localization.GetString("Messenger_File") : attachment.FileName,
            attachment.AttachmentSize,
            attachment.ImageWidth,
            attachment.ImageHeight);
    }

    private async Task<string> ResolveFileUrlAsync(string fileId) =>
        string.IsNullOrWhiteSpace(fileId) ? string.Empty : await _messenger.ResolveFileUrlAsync(fileId);

    private void RaiseCollectionDependentProperties()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
