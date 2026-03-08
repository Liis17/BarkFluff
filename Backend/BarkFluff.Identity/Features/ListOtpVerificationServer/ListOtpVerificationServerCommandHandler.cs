using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.ListOtpVerificationServer;

public class ListOtpVerificationServerCommandHandler : IRequestHandler<ListOtpVerificationServerCommand, ListOtpVerificationResponse>
{
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly ILogger<ListOtpVerificationServerCommandHandler> _logger;

    public ListOtpVerificationServerCommandHandler(AuthPropertiesStorage authPropertiesStorage,
        ILogger<ListOtpVerificationServerCommandHandler> logger)
    {
        _authPropertiesStorage = authPropertiesStorage;
        _logger = logger;
    }

    public async Task<ListOtpVerificationResponse> Handle(ListOtpVerificationServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение статуса 2FA для пользователя {UserId} (server)", request.UserId);

        var otpAuth = await _authPropertiesStorage.GetUserAuthProperties(request.UserId);

        if (otpAuth is null)
        {
            return new ListOtpVerificationResponse
            {
                AuthenticatorEnabled = false,
                EmailEnabled = false
            };
        }

        return new ListOtpVerificationResponse
        {
            EmailEnabled = otpAuth.EmailOtpEnabled,
            AuthenticatorEnabled = otpAuth.OtpEnabled
        };
    }
}
