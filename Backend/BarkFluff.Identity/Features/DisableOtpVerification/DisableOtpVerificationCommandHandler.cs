using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using OtpNet;
using OtpNotCreatedException = BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException;

namespace BarkFluff.Identity.Features.DisableOtpVerification;

public class DisableOtpVerificationCommandHandler : IRequestHandler<DisableOtpVerificationCommand, DisableOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;

    public DisableOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
    }

    public async Task<DisableOtpVerificationResponse> Handle(DisableOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        var otpConfigs = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

        if (otpConfigs is null)
        {
            throw new OtpNotCreatedException();
        }

        if (request.OptType == OtpTypeId.Authenticator)
        {
            if (!otpConfigs.OtpEnabled)
            {
                throw new OtpNotCreatedException();
            }
            
            var totp = new Totp(Base32Encoding.ToBytes(otpConfigs.OtpSecret));
            
            var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            if (!isValid)
            {
                throw new NotValidOtpCodeException();
            }

            await _authPropertiesStorage.DisableOtp(_userContext.UserId);
        }

        if (request.OptType == OtpTypeId.Email)
        {
            await _authPropertiesStorage.DisableEmailOtp(_userContext.UserId);
        }
        
        return new DisableOtpVerificationResponse();
    }
}