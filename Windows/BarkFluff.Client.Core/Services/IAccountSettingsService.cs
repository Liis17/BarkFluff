using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

/// <summary>
/// Правка собственного профиля и выход из аккаунта. Ошибка возвращается ключом словаря
/// локализации, <c>null</c> означает успех.
/// </summary>
public interface IAccountSettingsService
{
    Task<(string? ErrorKey, AccountProfile? Profile)> GetProfileAsync(CancellationToken cancellationToken = default);

    Task<string?> ChangeNameAsync(string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<string?> ChangeUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<string?> ChangeBioAsync(string bio, CancellationToken cancellationToken = default);

    /// <summary>Готовит изображение к отправке и загружает его как аватар.</summary>
    Task<string?> UploadAvatarAsync(string filePath, CancellationToken cancellationToken = default);

    Task<string?> LogoutAsync(CancellationToken cancellationToken = default);
}
