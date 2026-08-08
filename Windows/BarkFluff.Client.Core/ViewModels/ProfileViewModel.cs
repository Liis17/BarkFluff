using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Presence;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

/// <summary>
/// Один экран на два случая: свой профиль открывается из панели навигации (<see cref="LoadOwnAsync"/>),
/// профиль собеседника — из заголовка чата (<see cref="LoadForPeerAsync"/>), который уже знает chatId
/// и не должен резолвить его повторно.
/// </summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly IMessengerService _messenger;
    private readonly IOnlinePresenceService _presence;
    private readonly ILocalizationService _localization;
    private string? _chatId;

    public ProfileViewModel(IMessengerService messenger, IOnlinePresenceService presence, ILocalizationService localization)
    {
        _messenger = messenger;
        _presence = presence;
        _localization = localization;

        PhotosTab = new ProfileAttachmentsTabViewModel(messenger, localization, MessageAttachmentType.Image);
        VideosTab = new ProfileAttachmentsTabViewModel(messenger, localization, MessageAttachmentType.Video);
        FilesTab = new ProfileAttachmentsTabViewModel(messenger, localization, MessageAttachmentType.Document);
        VoiceTab = new ProfileAttachmentsTabViewModel(messenger, localization, MessageAttachmentType.Voice);
        _attachmentTabsByIndex = [PhotosTab, VideosTab, FilesTab, VoiceTab];
    }

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _avatarUrl = string.Empty;
    [ObservableProperty] private string _initials = string.Empty;
    [ObservableProperty] private string _registeredAtLabel = string.Empty;
    [ObservableProperty] private string _presenceLabel = string.Empty;
    [ObservableProperty] private bool _isOwnProfile;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int _selectedAttachmentTabIndex;

    public ProfileAttachmentsTabViewModel PhotosTab { get; }
    public ProfileAttachmentsTabViewModel VideosTab { get; }
    public ProfileAttachmentsTabViewModel FilesTab { get; }
    public ProfileAttachmentsTabViewModel VoiceTab { get; }

    private readonly ProfileAttachmentsTabViewModel[] _attachmentTabsByIndex;

    /// <summary>Чата с этим пользователем ещё нет (в т.ч. свой чат «избранное») — вкладкам вложений нечего показывать.</summary>
    public bool HasAttachments => !string.IsNullOrEmpty(_chatId);

    partial void OnSelectedAttachmentTabIndexChanged(int value) =>
        _ = _attachmentTabsByIndex[value].EnsureLoadedAsync();

    /// <summary>Профиль собеседника: chatId уже известен вызывающей стороне (шапка чата, SelectedChat.Id).</summary>
    public async Task LoadForPeerAsync(long peerUserId, string? chatId)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IsOwnProfile = false;
            if (!await LoadUserDataAsync(peerUserId))
            {
                return;
            }

            PresenceLabel = CreatePresenceLabel(peerUserId);
            await ApplyChatIdAsync(chatId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Собственный профиль: chatId — это чат с самим собой, сервер отдаёт его по своему userId.</summary>
    public async Task LoadOwnAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IsOwnProfile = true;
            if (!await LoadUserDataAsync(0))
            {
                return;
            }

            PresenceLabel = string.Empty;

            string? chatId = null;
            if (_messenger.CurrentUserId is { } currentUserId)
            {
                var (error, resolvedChatId) = await _messenger.GetPersonChatIdAsync(currentUserId);
                chatId = error.IsSuccess ? resolvedChatId : null;
            }

            await ApplyChatIdAsync(chatId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<bool> LoadUserDataAsync(long userId)
    {
        var (error, data) = await _messenger.GetUserDataAsync(userId);
        if (!error.IsSuccess || data is null)
        {
            ErrorMessage = _localization.GetString("Error_ProfileLoadFailed");
            return false;
        }

        var fullName = string.Join(' ', new[] { data.FirstName, data.LastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
        DisplayName = string.IsNullOrWhiteSpace(fullName) ? data.Username : fullName;
        Username = string.IsNullOrWhiteSpace(data.Username) ? string.Empty : "@" + data.Username;
        // Почту сервер отдаёт только для собственного профиля; пустая строка прячет строку в разметке.
        Email = data.Email;
        Description = data.Description;
        AvatarUrl = data.ProfilePicturePreviewUrl;
        Initials = CreateInitials(DisplayName);
        RegisteredAtLabel = data.RegistrationDate == DateTime.MinValue
            ? string.Empty
            : data.RegistrationDate.ToLocalTime().ToString("d");
        return true;
    }

    /// <summary>
    /// Стирает данные прошлой сессии. <see cref="ProfileViewModel"/> — синглтон внутри синглтона
    /// <c>MessengerViewModel</c>, поэтому без сброса при выходе из аккаунта следующий вошедший
    /// увидел бы в оверлее имя, аватар и вложения предыдущего пользователя.
    /// </summary>
    public void Reset()
    {
        DisplayName = string.Empty;
        Username = string.Empty;
        Email = string.Empty;
        Description = string.Empty;
        AvatarUrl = string.Empty;
        Initials = string.Empty;
        RegisteredAtLabel = string.Empty;
        PresenceLabel = string.Empty;
        IsOwnProfile = false;
        ErrorMessage = null;
        SelectedAttachmentTabIndex = 0;
        _chatId = null;
        OnPropertyChanged(nameof(HasAttachments));
        foreach (var tab in _attachmentTabsByIndex)
        {
            tab.Reset(null);
        }
    }

    private Task ApplyChatIdAsync(string? chatId)
    {
        _chatId = string.IsNullOrEmpty(chatId) ? null : chatId;
        OnPropertyChanged(nameof(HasAttachments));

        SelectedAttachmentTabIndex = 0;
        foreach (var tab in _attachmentTabsByIndex)
        {
            tab.Reset(_chatId);
        }

        return PhotosTab.EnsureLoadedAsync();
    }

    /// <summary>
    /// Статус берётся из кэша подписки мессенджера: заводить ради одного экрана вторую
    /// подписку на присутствие не нужно, все собеседники в ней уже есть.
    /// </summary>
    private string CreatePresenceLabel(long userId)
    {
        var presence = _presence.TryGet(userId);
        return presence is null
            ? string.Empty
            : LastSeenFormatter.Format(_localization, presence.IsOnline, presence.LastSeen, DateTimeOffset.Now);
    }

    private static string CreateInitials(string displayName) =>
        string.Concat(displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
}
