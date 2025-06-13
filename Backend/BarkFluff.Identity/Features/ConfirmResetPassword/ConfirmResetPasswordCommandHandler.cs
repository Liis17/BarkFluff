using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

using OtpNet;

using OtpType = BarkFluff.Identity.Domain.OtpType;

namespace BarkFluff.Identity.Features.ConfirmResetPassword
{
    using Services;

    public class ConfirmResetPasswordCommandHandler : IRequestHandler<ConfirmResetPasswordCommand>
    {
        private readonly ResetPasswordsStorage _resetPasswordsStorage;
        private readonly AuthPropertiesStorage _authPropertiesStorage;
        private readonly PasswordsStorage _passwordsStorage;

        public ConfirmResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage, AuthPropertiesStorage authPropertiesStorage, PasswordsStorage passwordsStorage)
        {
            _resetPasswordsStorage = resetPasswordsStorage;
            _authPropertiesStorage = authPropertiesStorage;
            _passwordsStorage = passwordsStorage;
        }

        public async Task Handle(ConfirmResetPasswordCommand request, CancellationToken cancellationToken)
        {
            var resetPasswordInfo = await _resetPasswordsStorage.GetResetPassword(request.ResetId);

            if (resetPasswordInfo is null)
            {
                throw new ResetIdNotFoundException();
            }

            if (resetPasswordInfo.IsApproved)
            {
                throw new ResetIdHasIsApprovedException();
            }

            if (resetPasswordInfo.OtpType == OtpType.Authenticator)
            {
                var otpSecret = await _authPropertiesStorage.GetOtpSecretKey(resetPasswordInfo.UserId);
                
                var totp = new Totp(Base32Encoding.ToBytes(otpSecret));
                
                var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

                if (!isValid)
                {
                    throw new NotValidOtpCodeException();
                }
            }
            else
            {
                if (!string.Equals(request.OtpCode, request.OtpCode))
                {
                    throw new NotValidOtpCodeException();
                }
            }

            var newPasswordHash = PasswordHasher.HashPassword(request.NewPassword);

            await _passwordsStorage.UpdateUserPasswordHash(resetPasswordInfo.UserId, newPasswordHash);
        }
    }
}