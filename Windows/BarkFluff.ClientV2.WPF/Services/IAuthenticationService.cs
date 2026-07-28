using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(string loginOrEmail, string password, string otpCode, CancellationToken cancellationToken = default);

    Task<FastAuthQrCode?> CreateFastAuthQrCodeAsync(CancellationToken cancellationToken = default);
}
