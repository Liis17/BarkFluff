using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public class AccountSettingsService : SessionScopedService, IAccountSettingsService
{
    private readonly IRealtimeMessengerService _realtimeMessenger;
    private readonly IOnlinePresenceService _presence;
    private readonly ISecureSessionStore _secureSessionStore;
    private readonly IPrivateChatKeyStore _privateChatKeyStore;
    private readonly MessengerViewModel _messengerViewModel;
    private readonly IOnboardingNavigationService _navigation;

    public AccountSettingsService(
        WebApiClient webApi,
        IClientSession session,
        IRealtimeMessengerService realtimeMessenger,
        IOnlinePresenceService presence,
        ISecureSessionStore secureSessionStore,
        IPrivateChatKeyStore privateChatKeyStore,
        MessengerViewModel messengerViewModel,
        IOnboardingNavigationService navigation)
        : base(webApi, session)
    {
        _realtimeMessenger = realtimeMessenger;
        _presence = presence;
        _secureSessionStore = secureSessionStore;
        _privateChatKeyStore = privateChatKeyStore;
        _messengerViewModel = messengerViewModel;
        _navigation = navigation;
    }

    public async Task<(string? ErrorKey, AccountProfile? Profile)> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var (error, data) = await WebApi.GetUserData(Parameters);
        if (!error.IsSuccess || data is null)
        {
            return ("Error_SettingsLoadFailed", null);
        }

        return (null, new AccountProfile(
            data.FirstName,
            data.LastName,
            data.Username,
            data.Description,
            data.ProfilePicturePreviewUrl));
    }

    public async Task<string?> ChangeNameAsync(string firstName, string lastName, CancellationToken cancellationToken = default) =>
        ToErrorKey(await WebApi.ChangeName(firstName, lastName, Parameters));

    public async Task<string?> ChangeUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var parameters = Parameters;
        var (checkError, isTaken) = await WebApi.CheckUsername(username, parameters);
        if (!checkError.IsSuccess)
        {
            return "Error_SettingsSaveFailed";
        }

        if (isTaken)
        {
            return "Error_UsernameTaken";
        }

        return ToErrorKey(await WebApi.ChangeUsername(username, parameters));
    }

    public async Task<string?> ChangeBioAsync(string bio, CancellationToken cancellationToken = default) =>
        ToErrorKey(await WebApi.ChangeBio(bio, Parameters));

    /// <summary>
    /// Кадрирование пока не выполняется: изображение уходит целиком, только приводится к JPEG
    /// и уменьшается до допустимого размера. Обрезку добавит будущий кроппер.
    /// </summary>
    public async Task<string?> UploadAvatarAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var parameters = Parameters;
        string preparedPath;
        try
        {
            preparedPath = await ImageProcessor.ProcessImageForUploadAsync(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return "Error_SettingsSaveFailed";
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(preparedPath, cancellationToken);
            return ToErrorKey(await WebApi.UploadUserAvatarAsync(parameters, bytes));
        }
        catch (IOException)
        {
            return "Error_SettingsSaveFailed";
        }
        finally
        {
            DeleteTemporaryCopy(filePath, preparedPath);
        }
    }

    /// <summary>
    /// Порядок шагов важен: стримы останавливаются первыми, чтобы не переподключаться с уже
    /// отозванным токеном, отзыв на сервере идёт до очистки локальных токенов — без них
    /// вызвать его будет нечем, — и лишь затем стираются секреты этого устройства.
    /// </summary>
    public async Task<string?> LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _realtimeMessenger.StopAsync();
        await _presence.StopAsync();

        var result = await LogoutFromServerAsync(Parameters);

        await _secureSessionStore.ClearAsync(cancellationToken);
        await _privateChatKeyStore.ForgetAllAsync(cancellationToken);
        _messengerViewModel.Reset();
        _navigation.ShowLogin();

        return result.IsSuccess ? null : "Error_LogoutFailed";
    }

    /// <summary>Отдельная точка для проверки порядка выхода без подключения к живой ноде.</summary>
    protected virtual Task<ErrorReturner> LogoutFromServerAsync(GlobalParam parameters) => WebApi.Logout(parameters);

    private static void DeleteTemporaryCopy(string originalPath, string preparedPath)
    {
        if (string.Equals(originalPath, preparedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(preparedPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Временный файл останется в каталоге системы — это не повод срывать сохранение аватара.
        }
    }

    private static string? ToErrorKey(ErrorReturner result) => result.IsSuccess ? null : "Error_SettingsSaveFailed";
}
