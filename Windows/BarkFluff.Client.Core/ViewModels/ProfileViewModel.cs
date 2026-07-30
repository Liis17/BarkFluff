using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Presence;
using BarkFluff.Client.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

/// <summary>
/// Один экран на два случая: свой профиль открывается из панели навигации, профиль собеседника —
/// из заголовка чата. Различает их только идентификатор, переданный в <see cref="LoadAsync"/>.
/// </summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly IMessengerService _messenger;
    private readonly IOnlinePresenceService _presence;
    private readonly ILocalizationService _localization;

    public ProfileViewModel(IMessengerService messenger, IOnlinePresenceService presence, ILocalizationService localization)
    {
        _messenger = messenger;
        _presence = presence;
        _localization = localization;
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

    /// <param name="userId">Ноль означает собственный профиль — так же трактует запрос сервер.</param>
    public async Task LoadAsync(long userId)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IsOwnProfile = userId == 0 || userId == _messenger.CurrentUserId;
            var (error, data) = await _messenger.GetUserDataAsync(userId);
            if (!error.IsSuccess || data is null)
            {
                ErrorMessage = _localization.GetString("Error_ProfileLoadFailed");
                return;
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
            PresenceLabel = IsOwnProfile ? string.Empty : CreatePresenceLabel(data.Id);
        }
        finally
        {
            IsLoading = false;
        }
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
