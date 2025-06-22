using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

using OtpNet;

using OtpType = BarkFluff.Identity.Domain.OtpType;

namespace BarkFluff.Identity.Features.ConfirmResetPassword
{
    using CreateToken;
    using Google.Protobuf.WellKnownTypes;
    using GrpcServer.Tracker;
    using Proto.Identity;
    using Services;

    public class ConfirmResetPasswordCommandHandler : IRequestHandler<ConfirmResetPasswordCommand, ConfirmResetPasswordResponse>
    {
        private readonly ResetPasswordsStorage _resetPasswordsStorage;
        private readonly AuthPropertiesStorage _authPropertiesStorage;
        private readonly PasswordsStorage _passwordsStorage;
        private readonly RefreshTokensStorage refreshTokensStorage;
        private readonly IMediator _mediator;
        private readonly RequestContext requestContext;
        
        private const int ExpDaysRefreshToken = 9999;


        public ConfirmResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage, AuthPropertiesStorage authPropertiesStorage,
            PasswordsStorage passwordsStorage, RefreshTokensStorage refreshTokensStorage, IMediator mediator, RequestContext requestContext)
        {
            _resetPasswordsStorage = resetPasswordsStorage;
            _authPropertiesStorage = authPropertiesStorage;
            _passwordsStorage = passwordsStorage;
            this.refreshTokensStorage = refreshTokensStorage;
            _mediator = mediator;
            this.requestContext = requestContext;
        }

        public async Task<ConfirmResetPasswordResponse> Handle(ConfirmResetPasswordCommand request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(requestContext.DeviceName))
            {
                throw new XDeviceNameIsRequiredException();
            }

            if (string.IsNullOrEmpty(requestContext.OperationSystem))
            {
                throw new XOsNameIsRequiredException();
            }

            if (string.IsNullOrEmpty(requestContext.AppName) || string.IsNullOrEmpty(requestContext.AppVersion))
            {
                throw new XAppInfoIsRequiedException();
            }
            
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
            
            var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
            await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, resetPasswordInfo.UserId, requestContext.DeviceName, ExpDaysRefreshToken);

            var accessTokenResponse = await _mediator.Send(new CreateTokenCommand { RefreshToken = refreshTokenString }, cancellationToken);

            return new ConfirmResetPasswordResponse()
            {
                AccessToken = accessTokenResponse.AccessToken,
                RefreshToken = new Token
                {
                    ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken)),
                    Value = refreshTokenString
                }
            };
        }
    }
}